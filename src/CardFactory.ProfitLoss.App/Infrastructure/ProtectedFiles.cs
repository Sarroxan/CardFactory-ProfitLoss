using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CardFactory.ProfitLoss.App.Infrastructure;

/// <summary>
/// Owner, 16 Nov 2026 (option 1): every file the app keeps on the PC is encrypted, the same way
/// as the saved password (<see cref="Services.CredentialStore"/>) - Windows DPAPI for the
/// signed-in Windows user, so the files can't be read from another account or another PC.
/// A file written in plain text by an earlier version is read once and written back encrypted.
/// </summary>
internal static class ProtectedFiles
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CardFactory.ProfitLoss.Files.v1");

    // Every DPAPI blob starts with version 1 and the DPAPI provider's GUID
    // {DF9D8CD0-1501-11D1-8C7A-00C04FC297EB}: that tells encrypted from plain text.
    private static readonly byte[] BlobStart =
    {
        0x01, 0x00, 0x00, 0x00, 0xD0, 0x8C, 0x9D, 0xDF, 0x01, 0x15, 0xD1, 0x11, 0x8C, 0x7A, 0x00, 0xC0, 0x4F, 0xC2, 0x97, 0xEB
    };

    public static bool IsEncrypted(byte[] bytes) =>
        bytes.Length > BlobStart.Length && bytes.AsSpan(0, BlobStart.Length).SequenceEqual(BlobStart);

    public static byte[] Protect(string text) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.CurrentUser);

    public static string Unprotect(byte[] blob) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The file's text, or null when there is no file or it can't be read here.</summary>
    public static string? ReadAllText(string path)
    {
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        if (IsEncrypted(bytes))
        {
            try { return Unprotect(bytes); }
            catch (CryptographicException) { return null; }   // another Windows user's, or damaged
        }

        // Plain text from an earlier version: lock it from now on.
        var text = PlainText(bytes);
        try { WriteAllText(path, text); } catch { }
        return text;
    }

    /// <summary>Writes the text encrypted, through a temporary file, so a half-written file is never left.</summary>
    public static void WriteAllText(string path, string text)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, Protect(text));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>At start-up: a plain-text file left by an earlier version is encrypted where it is.</summary>
    public static void EnsureEncrypted(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var bytes = File.ReadAllBytes(path);
            if (!IsEncrypted(bytes)) WriteAllText(path, PlainText(bytes));
        }
        catch
        {
            // Tidying must never stop the app starting.
        }
    }

    private static string PlainText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
