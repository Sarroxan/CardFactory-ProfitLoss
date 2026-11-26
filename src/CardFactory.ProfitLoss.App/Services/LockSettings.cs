using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardFactory.ProfitLoss.App.Infrastructure;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>The lock (owner, 26 Nov 2026, concept P1): on/off, after how long without use, and the
/// 4-digit PIN - kept as a salted hash inside the app's encrypted file (DPAPI), never as the digits.</summary>
public sealed class LockChoices
{
    public bool Enabled { get; set; }
    public int Minutes { get; set; } = 60;
    public string? PinSalt { get; set; }
    public string? PinHash { get; set; }

    public bool HasPin => !string.IsNullOrEmpty(PinSalt) && !string.IsNullOrEmpty(PinHash);
}

public static class LockSettings
{
    private static string FilePath => Path.Combine(AppFiles.Folder, "lock-settings.json");

    public static LockChoices Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new LockChoices();
            var loaded = JsonSerializer.Deserialize<LockChoices>(ProtectedFiles.ReadAllText(FilePath) ?? "null") ?? new LockChoices();
            loaded.Minutes = Math.Clamp(loaded.Minutes, 5, 480);
            if (!loaded.HasPin) loaded.Enabled = false;
            return loaded;
        }
        catch
        {
            return new LockChoices();
        }
    }

    public static void Save(LockChoices choices)
    {
        try
        {
            Directory.CreateDirectory(AppFiles.Folder);
            ProtectedFiles.WriteAllText(FilePath, JsonSerializer.Serialize(choices));
        }
        catch
        {
            // Not kept this time.
        }
    }

    public static bool IsPin(string? text) => text is { Length: 4 } && text.All(char.IsDigit);

    public static void SetPin(LockChoices choices, string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        choices.PinSalt = Convert.ToBase64String(salt);
        choices.PinHash = Hash(salt, pin);
    }

    public static bool CheckPin(LockChoices choices, string pin)
    {
        if (!choices.HasPin || !IsPin(pin)) return false;
        try
        {
            var expected = Convert.FromBase64String(choices.PinHash!);
            var actual = Convert.FromBase64String(Hash(Convert.FromBase64String(choices.PinSalt!), pin));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    private static string Hash(byte[] salt, string pin) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, 100_000, HashAlgorithmName.SHA256, 32));
}
