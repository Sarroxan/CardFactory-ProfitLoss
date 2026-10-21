namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// Stamps sold, for Reports and the Daily Briefing's Stamp Count (owner, 21 Oct 2026, R1 + B1).
/// From Item Sales By Operator with Product Groups = Stamps. Each line is matched by its
/// description: "1ST"/"First" class, "2ND"/"Second" class, or "Large" (Large wins, so a
/// "Large 1st Class" line counts as Large). Counts are Flooid's quantity: a book of 8 is 1.
/// Christmas stamps are not recorded at all (owner, 21 Oct 2026): any line naming Christmas or
/// Xmas is skipped. Anything else unmatched stays in <see cref="Other"/>.
/// </summary>
public sealed record StampsSold(int FirstClass, int SecondClass, int Large, int Other)
{
    public int Total => FirstClass + SecondClass + Large + Other;

    public static StampsSold None { get; } = new(0, 0, 0, 0);

    public static StampsSold From(IEnumerable<GiftCardItemRecord> items)
    {
        int first = 0, second = 0, large = 0, other = 0;
        foreach (var item in items)
        {
            if (IsChristmas(item.Description)) continue;
            switch (Classify(item.Description))
            {
                case "Large": large += item.Quantity; break;
                case "1st": first += item.Quantity; break;
                case "2nd": second += item.Quantity; break;
                default: other += item.Quantity; break;
            }
        }
        return new StampsSold(first, second, large, other);
    }

    public static bool IsChristmas(string? description) =>
        (description ?? string.Empty).Contains("christmas", StringComparison.OrdinalIgnoreCase)
        || (description ?? string.Empty).Contains("xmas", StringComparison.OrdinalIgnoreCase);

    /// <returns>"Large", "1st", "2nd", or null.</returns>
    public static string? Classify(string? description)
    {
        var d = description ?? string.Empty;
        bool Has(string word) => d.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (Has("large")) return "Large";
        if (Has("1st") || Has("first")) return "1st";
        if (Has("2nd") || Has("second")) return "2nd";
        return null;
    }
}
