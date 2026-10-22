using System.Globalization;
using System.Text.Json;

namespace CardFactory.ProfitLoss.Core.Services;

/// <summary>Last year's Team figures for one day or range: what the targets are worked out from.</summary>
public sealed record LastYearFigures(int Transactions, decimal Abv, decimal Aub);

/// <summary>
/// Last year's figures kept on the till (owner, 22 Oct 2026, option A). The till's log showed
/// last year's Team and Gift Cards pulls taking 11 of the 22 seconds after signing in, on every
/// run of the app, though last year can't change. Kept per store and per last-year day or range,
/// in one small JSON file; a missing, damaged or older-format file is simply ignored and the
/// figures are pulled from Flooid as before.
/// </summary>
public sealed class LastYearFigureStore
{
    // Bump when how the figures are worked out changes, so old ones are never reused.
    private const int FormatVersion = 1;
    private const int MaxEntries = 2000;

    private readonly string _path;

    public LastYearFigureStore(string path) => _path = path;

    public bool TryGet(string? store, DateTime from, DateTime to, out LastYearFigures figures)
    {
        figures = new LastYearFigures(0, 0m, 0m);
        var key = Key(store, from, to);
        if (key is null) return false;
        var file = Load();
        if (file?.Entries is null || !file.Entries.TryGetValue(key, out var entry)) return false;
        figures = new LastYearFigures(entry.Transactions, entry.Abv, entry.Aub);
        return true;
    }

    public void Save(string? store, DateTime from, DateTime to, LastYearFigures figures)
    {
        var key = Key(store, from, to);
        if (key is null) return;
        try
        {
            var file = Load() ?? new StoreFile { Version = FormatVersion };
            file.Entries ??= new Dictionary<string, Entry>();
            file.Entries[key] = new Entry { Transactions = figures.Transactions, Abv = figures.Abv, Aub = figures.Aub, Saved = DateTime.Now };
            if (file.Entries.Count > MaxEntries)
            {
                foreach (var old in file.Entries.OrderBy(e => e.Value.Saved).Take(file.Entries.Count - MaxEntries).Select(e => e.Key).ToList())
                    file.Entries.Remove(old);
            }
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file));
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            // Only a convenience: the next run pulls the figures from Flooid again.
        }
    }

    /// <summary>No store name yet (it is read just after signing in): nothing is kept or used.</summary>
    private static string? Key(string? store, DateTime from, DateTime to)
    {
        var name = (store ?? string.Empty).Trim();
        if (name.Length == 0) return null;
        return name.ToUpperInvariant() + "|" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + "|" + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private StoreFile? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_path));
            return file is { Version: FormatVersion } ? file : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class StoreFile
    {
        public int Version { get; set; }
        public Dictionary<string, Entry>? Entries { get; set; }
    }

    private sealed class Entry
    {
        public int Transactions { get; set; }
        public decimal Abv { get; set; }
        public decimal Aub { get; set; }
        public DateTime Saved { get; set; }
    }
}
