using System.IO;
using System.Text.Json;
using CardFactory.ProfitLoss.App.Infrastructure;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>
/// Add-on Sales (owner, 16 Nov 2026): the add-ons this till tracks, added by SKU in the Add-on
/// Sales tab, and every item seen in Flooid's reports so the SKU box can offer names as you type.
/// Both kept in the app's own folder; a missing or unreadable file means Stamps only and no names.
/// </summary>
public static class AddOnSettings
{
    private static string TrackedPath => Path.Combine(AppFiles.Folder, "add-ons.json");
    private static string SeenPath => Path.Combine(AppFiles.Folder, "add-on-items-seen.json");

    /// <summary>Most items kept for the SKU box, newest first, so the file never grows without end.</summary>
    public const int SeenLimit = 20000;

    private sealed class TrackedEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public static List<AddOnItem> LoadTracked()
    {
        try
        {
            if (!File.Exists(TrackedPath)) return new List<AddOnItem> { AddOnItem.Stamps };
            var entries = JsonSerializer.Deserialize<List<TrackedEntry>>(File.ReadAllText(TrackedPath)) ?? new();
            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .Select(e => new AddOnItem(e.Key.Trim(), string.IsNullOrWhiteSpace(e.Name) ? e.Key.Trim() : e.Name.Trim()))
                .GroupBy(e => e.Key.ToUpperInvariant())
                .Select(g => g.First())
                .ToList();
        }
        catch
        {
            return new List<AddOnItem> { AddOnItem.Stamps };
        }
    }

    public static void SaveTracked(IEnumerable<AddOnItem> items)
    {
        try
        {
            Directory.CreateDirectory(AppFiles.Folder);
            var entries = items.Select(i => new TrackedEntry { Key = i.Key, Name = i.Name }).ToList();
            File.WriteAllText(TrackedPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not kept this time; the list on screen is still used.
        }
    }

    /// <summary>SKU → description, for every item seen in the all-items report on this till.</summary>
    public static Dictionary<string, string> LoadSeen()
    {
        try
        {
            if (!File.Exists(SeenPath)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SeenPath));
            return loaded is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Adds the items from a new report (their latest description wins) and keeps the file.</summary>
    public static Dictionary<string, string> RememberSeen(Dictionary<string, string> seen, IEnumerable<GiftCardItemRecord> items)
    {
        var changed = false;
        foreach (var item in items)
        {
            var code = (item.ProductCode ?? string.Empty).Trim();
            var name = (item.Description ?? string.Empty).Trim();
            if (code.Length == 0 || name.Length == 0) continue;
            if (seen.TryGetValue(code, out var known) && known == name) continue;
            seen.Remove(code);
            seen[code] = name;
            changed = true;
        }

        if (seen.Count > SeenLimit)
        {
            // The oldest entries were added first; keep the newest.
            var newest = seen.Skip(seen.Count - SeenLimit).ToList();
            seen = new Dictionary<string, string>(newest, StringComparer.OrdinalIgnoreCase);
            changed = true;
        }

        if (!changed) return seen;
        try
        {
            Directory.CreateDirectory(AppFiles.Folder);
            File.WriteAllText(SeenPath, JsonSerializer.Serialize(seen));
        }
        catch
        {
            // Names are only a help for typing; nothing else depends on them.
        }
        return seen;
    }
}
