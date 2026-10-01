using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// Stage 6B.19. Optional storage for the Flooid sign-in, so an expired session can be
/// re-established without typing.
///
/// Encrypted with Windows DPAPI at CurrentUser scope, which ties the ciphertext to this
/// Windows account on this machine: copying the file to another PC, or another user on
/// the same PC opening it, both fail to decrypt. That is the right bar for a back-office
/// login on a store computer. It is not a secret vault and is not claimed to be - anything
/// running as this user could ask DPAPI to decrypt it too.
///
/// Off by default, and 6A.92 clears everything else on exit, so this is the one thing the
/// application deliberately keeps. It is written only when the box is ticked and deleted
/// the moment it is unticked.
/// </summary>
public static class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CardFactory.ProfitLoss.Flooid.v1");

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardFactory-ProfitLoss", "flooid-credentials.dat");

    public static bool Exists
    {
        get
        {
            try { return File.Exists(StorePath); }
            catch { return false; }
        }
    }

    public static bool TrySave(string username, string password)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(username + "\u001f" + password);
            var encrypted = ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllBytes(StorePath, encrypted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryLoad(out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        try
        {
            if (!File.Exists(StorePath)) return false;
            var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(StorePath), Entropy, DataProtectionScope.CurrentUser);
            var parts = Encoding.UTF8.GetString(decrypted).Split('\u001f');
            if (parts.Length != 2) return false;
            username = parts[0];
            password = parts[1];
            return username.Length > 0 && password.Length > 0;
        }
        catch
        {
            // Wrong machine, wrong user, or a corrupt file. Treat all three the same: there
            // is nothing usable, so the person types it in.
            return false;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StorePath)) File.Delete(StorePath);
        }
        catch
        {
        }
    }
}
