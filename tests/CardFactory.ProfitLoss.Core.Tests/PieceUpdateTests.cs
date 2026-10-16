using CardFactory.ProfitLoss.Core.Updates;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class PieceUpdateTests
{
    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    // A "new version": the old file with a few kilobytes changed in the middle and some inserted,
    // which shifts everything after it - the case fixed-size pieces would handle badly.
    private static byte[] Edited(byte[] old)
    {
        var changed = RandomBytes(3000, 99);
        var middle = old.Length / 2;
        return old.Take(middle).Concat(changed).Concat(old.Skip(middle + 1000)).ToArray();
    }

    private static Dictionary<long, byte[]> Download(byte[] file, PiecePlan plan) =>
        plan.Downloads.ToDictionary(d => d.Offset, d => file.AsSpan((int)d.Offset, (int)d.Length).ToArray());

    [Fact]
    public void Split_CoversTheWholeFileWithinTheSizeLimits()
    {
        var data = RandomBytes(2_000_000, 1);
        var pieces = ContentChunker.Split(data);

        Assert.Equal(0, pieces[0].Offset);
        Assert.Equal(data.Length, pieces.Sum(p => (long)p.Length));
        for (var i = 1; i < pieces.Count; i++) Assert.Equal(pieces[i - 1].Offset + pieces[i - 1].Length, pieces[i].Offset);
        Assert.All(pieces.Take(pieces.Count - 1), p => Assert.InRange(p.Length, ContentChunker.DefaultMin, ContentChunker.DefaultMax));
    }

    [Fact]
    public void Split_IsTheSameEveryTime()
    {
        var data = RandomBytes(1_000_000, 2);
        Assert.Equal(ContentChunker.Split(data), ContentChunker.Split(data));
    }

    [Fact]
    public void Plan_SameFile_NeedsNothing()
    {
        var file = RandomBytes(1_500_000, 3);
        var plan = PieceUpdate.Plan(file, PieceManifest.Create(file, "1.0.2"));

        Assert.Empty(plan.Downloads);
        Assert.Equal(0, plan.DownloadBytes);
    }

    [Fact]
    public void Plan_SmallEdit_NeedsOnlyAFewPieces_AndRebuildsTheNewFileExactly()
    {
        var old = RandomBytes(3_000_000, 4);
        var updated = Edited(old);
        var manifest = PieceManifest.FromJson(PieceManifest.Create(updated, "1.0.2").ToJson());

        var plan = PieceUpdate.Plan(old, manifest);
        Assert.InRange(plan.DownloadBytes, 1, 4 * ContentChunker.DefaultMax);

        using var output = new MemoryStream();
        PieceUpdate.Assemble(old, manifest, plan, Download(updated, plan), output);
        Assert.Equal(updated, output.ToArray());
    }

    [Fact]
    public void Assemble_WrongDownload_IsRefused()
    {
        var old = RandomBytes(2_000_000, 5);
        var updated = Edited(old);
        var manifest = PieceManifest.Create(updated, "1.0.2");
        var plan = PieceUpdate.Plan(old, manifest);
        var downloaded = Download(updated, plan);
        downloaded.First().Value[10] ^= 0xFF;   // one byte damaged in transit

        Assert.Throws<InvalidDataException>(() => PieceUpdate.Assemble(old, manifest, plan, downloaded, new MemoryStream()));
    }

    [Fact]
    public void Assemble_MissingDownload_IsRefused()
    {
        var old = RandomBytes(2_000_000, 6);
        var manifest = PieceManifest.Create(Edited(old), "1.0.2");
        var plan = PieceUpdate.Plan(old, manifest);

        Assert.Throws<InvalidDataException>(() => PieceUpdate.Assemble(old, manifest, plan, new Dictionary<long, byte[]>(), new MemoryStream()));
    }

    [Fact]
    public void FromJson_RejectsAListWithAGap()
    {
        var manifest = PieceManifest.Create(RandomBytes(500_000, 7), "1.0.2");
        var broken = manifest with { Pieces = manifest.Pieces.Skip(1).ToList() };

        Assert.Throws<InvalidDataException>(() => PieceManifest.FromJson(broken.ToJson()));
    }
}
