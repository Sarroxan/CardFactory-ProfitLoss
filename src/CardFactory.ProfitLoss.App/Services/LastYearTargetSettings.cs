using System.Globalization;
using System.IO;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.Core.Services;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// Menu → Settings → Targets from last year (owner, 16 Oct 2026, concept T2). Kept in the app's
/// own folder, beside the Fast/Safe file:
///
/// CardFactory-PL-lastyear-targets.txt
///     enabled=true
///     abv=10
///     aub=0
///
/// A missing or unreadable file means the defaults: on, ABV +10%, AUB +0%.
/// </summary>
public static class LastYearTargetSettings
{
    public const string SettingsFileName = "CardFactory-PL-lastyear-targets.txt";

    public static bool Enabled { get; private set; } = true;
    public static decimal AbvPercent { get; private set; } = LastYearTargetCalculator.DefaultAbvPercent;
    public static decimal AubPercent { get; private set; } = LastYearTargetCalculator.DefaultAubPercent;

    private static string FilePath => Path.Combine(AppFiles.Folder, SettingsFileName);

    public static void Load()
    {
        Enabled = true;
        AbvPercent = LastYearTargetCalculator.DefaultAbvPercent;
        AubPercent = LastYearTargetCalculator.DefaultAubPercent;
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                switch (parts[0].ToLowerInvariant())
                {
                    case "enabled":
                        if (bool.TryParse(parts[1], out var on)) Enabled = on;
                        break;
                    case "abv":
                        if (TryPercent(parts[1], out var abv)) AbvPercent = abv;
                        break;
                    case "aub":
                        if (TryPercent(parts[1], out var aub)) AubPercent = aub;
                        break;
                }
            }
        }
        catch
        {
            // Unreadable: the defaults set above stand.
        }
    }

    public static bool TrySave(bool enabled, decimal abvPercent, decimal aubPercent)
    {
        try
        {
            Directory.CreateDirectory(AppFiles.Folder);
            File.WriteAllLines(FilePath, new[]
            {
                "enabled=" + (enabled ? "true" : "false"),
                "abv=" + abvPercent.ToString("0.##", CultureInfo.InvariantCulture),
                "aub=" + aubPercent.ToString("0.##", CultureInfo.InvariantCulture)
            });
            Enabled = enabled;
            AbvPercent = abvPercent;
            AubPercent = aubPercent;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>"+10", "10", "-5", "0", "+2.5" → a percentage within the allowed limits.</summary>
    public static bool TryPercent(string? text, out decimal percent)
    {
        percent = 0m;
        var cleaned = (text ?? string.Empty).Trim().TrimEnd('%').Trim();
        if (cleaned.StartsWith('+')) cleaned = cleaned[1..].Trim();
        if (cleaned.Length == 0) return false;
        if (!decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return false;
        if (value < LastYearTargetCalculator.MinPercent || value > LastYearTargetCalculator.MaxPercent) return false;
        percent = value;
        return true;
    }

    /// <summary>10 → "+10%", 0 → "+0%", -5 → "-5%".</summary>
    public static string Signed(decimal percent) =>
        (percent >= 0 ? "+" : "-") + Math.Abs(percent).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public static string Summary =>
        Enabled
            ? $"On · ABV {Signed(AbvPercent)} · AUB {Signed(AubPercent)}"
            : "Off · targets typed by hand";
}
