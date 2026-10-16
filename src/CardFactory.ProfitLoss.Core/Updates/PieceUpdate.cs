using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardFactory.ProfitLoss.Core.Updates;

/// <summary>
/// Small updates (owner, 16 Oct 2026). The app is one large file (about 142 MB, the .NET and
/// WPF runtime carried inside it), but an update changes only a little of it: between the real
/// 1.0.0 and 1.0.1 files, 1.2 MB in 8 stretches. So each release publishes a piece list of
/// its app file, and the updater downloads only the pieces the running copy doesn't already
/// have, then rebuilds the new file and checks its fingerprint before it is used.
///
/// Pieces are cut where the content says, not at fixed sizes (content-defined chunking with a
/// gear hash), so a change in one place doesn't move every boundary after it. The cutting must
/// be identical on the build machine and on the till: same table, same sizes, same rule.
/// </summary>
public static class ContentChunker
{
    public const int DefaultMin = 8 * 1024;
    public const int DefaultAverage = 32 * 1024;   // a power of two
    public const int DefaultMax = 128 * 1024;

    private static readonly ulong[] Gear = BuildGear();

    private static ulong[] BuildGear()
    {
        var gear = new ulong[256];
        var state = 0x9E3779B97F4A7C15UL;
        for (var i = 0; i < gear.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            gear[i] = state;
        }
        return gear;
    }

    /// <summary>Piece boundaries as (offset, length), covering the whole of <paramref name="data"/>.</summary>
    public static List<(long Offset, int Length)> Split(ReadOnlySpan<byte> data, int min = DefaultMin, int average = DefaultAverage, int max = DefaultMax)
    {
        if (min <= 0 || max < min || average <= 0 || (average & (average - 1)) != 0)
            throw new ArgumentException("Piece sizes must be positive, max at least min, and the average a power of two.");

        var mask = (ulong)(average - 1) << 16;
        var pieces = new List<(long, int)>();
        var n = data.Length;
        var start = 0;
        while (start < n)
        {
            ulong hash = 0;
            var i = Math.Min(start + min, n);
            var end = n;
            for (; i < n && i - start < max; i++)
            {
                hash = (hash << 1) + Gear[data[i]];
                if ((hash & mask) == 0) { end = i + 1; break; }
            }
            if (i >= n) end = n;
            else if (i - start >= max) end = i;
            pieces.Add((start, end - start));
            start = end;
        }
        return pieces;
    }

    /// <summary>A piece's identity: the first 16 bytes of its SHA-256, as hex.</summary>
    public static string PieceHash(ReadOnlySpan<byte> piece) =>
        Convert.ToHexString(SHA256.HashData(piece), 0, 16).ToLowerInvariant();
}

public sealed record Piece(
    [property: JsonPropertyName("o")] long Offset,
    [property: JsonPropertyName("l")] int Length,
    [property: JsonPropertyName("h")] string Hash);

/// <summary>The piece list published with each release beside the app file.</summary>
public sealed record PieceManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("avg")] int Average,
    [property: JsonPropertyName("max")] int Max,
    [property: JsonPropertyName("pieces")] IReadOnlyList<Piece> Pieces)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static PieceManifest Create(byte[] file, string version)
    {
        var pieces = ContentChunker.Split(file)
            .Select(p => new Piece(p.Offset, p.Length, ContentChunker.PieceHash(file.AsSpan((int)p.Offset, p.Length))))
            .ToList();
        return new PieceManifest(version, file.LongLength, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(),
            ContentChunker.DefaultMin, ContentChunker.DefaultAverage, ContentChunker.DefaultMax, pieces);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static PieceManifest FromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<PieceManifest>(json, Json)
            ?? throw new InvalidDataException("The piece list is empty.");
        if (manifest.Pieces is null || manifest.Pieces.Count == 0 || manifest.Size <= 0 || string.IsNullOrWhiteSpace(manifest.Sha256))
            throw new InvalidDataException("The piece list is incomplete.");
        long expected = 0;
        foreach (var piece in manifest.Pieces)
        {
            if (piece.Offset != expected || piece.Length <= 0) throw new InvalidDataException("The piece list does not cover the file in order.");
            expected += piece.Length;
        }
        if (expected != manifest.Size) throw new InvalidDataException("The piece list does not add up to the file size.");
        return manifest;
    }
}

/// <summary>What to download (ranges of the new file) and where reused pieces sit in the local file.</summary>
public sealed record PiecePlan(
    IReadOnlyList<(long Offset, long Length)> Downloads,
    long DownloadBytes,
    IReadOnlyDictionary<string, (long Offset, int Length)> Local);

public static class PieceUpdate
{
    /// <summary>Cuts the local file the same way as the manifest and lists the pieces it lacks,
    /// merged into as few ranges as possible.</summary>
    public static PiecePlan Plan(byte[] local, PieceManifest manifest)
    {
        var have = new Dictionary<string, (long, int)>(StringComparer.Ordinal);
        foreach (var (offset, length) in ContentChunker.Split(local, manifest.Min, manifest.Average, manifest.Max))
            have.TryAdd(ContentChunker.PieceHash(local.AsSpan((int)offset, length)), (offset, length));

        var downloads = new List<(long Offset, long Length)>();
        foreach (var piece in manifest.Pieces)
        {
            if (have.ContainsKey(piece.Hash)) continue;
            if (downloads.Count > 0 && downloads[^1].Offset + downloads[^1].Length == piece.Offset)
                downloads[^1] = (downloads[^1].Offset, downloads[^1].Length + piece.Length);
            else
                downloads.Add((piece.Offset, piece.Length));
        }
        return new PiecePlan(downloads, downloads.Sum(d => d.Length), have);
    }

    /// <summary>
    /// Writes the new file to <paramref name="output"/> from reused local pieces and the downloaded
    /// ranges (keyed by their offset in the new file). Every piece and the whole file are checked;
    /// any mismatch throws, so a wrong file is never used.
    /// </summary>
    public static void Assemble(byte[] local, PieceManifest manifest, PiecePlan plan,
        IReadOnlyDictionary<long, byte[]> downloaded, Stream output)
    {
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var piece in manifest.Pieces)
        {
            ReadOnlySpan<byte> bytes;
            if (plan.Local.TryGetValue(piece.Hash, out var at) && at.Length == piece.Length)
            {
                bytes = local.AsSpan((int)at.Offset, at.Length);
            }
            else
            {
                var range = plan.Downloads.FirstOrDefault(d => piece.Offset >= d.Offset && piece.Offset + piece.Length <= d.Offset + d.Length);
                if (range.Length == 0 || !downloaded.TryGetValue(range.Offset, out var data) || data.LongLength != range.Length)
                    throw new InvalidDataException($"Piece at {piece.Offset} was not downloaded.");
                bytes = data.AsSpan((int)(piece.Offset - range.Offset), piece.Length);
            }

            if (ContentChunker.PieceHash(bytes) != piece.Hash)
                throw new InvalidDataException($"Piece at {piece.Offset} does not match the piece list.");
            output.Write(bytes);
            whole.AppendData(bytes);
        }

        var actual = Convert.ToHexString(whole.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The rebuilt file does not match its fingerprint.");
    }
}
