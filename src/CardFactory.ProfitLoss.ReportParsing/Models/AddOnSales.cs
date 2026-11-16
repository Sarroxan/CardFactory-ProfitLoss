namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// An add-on the store tracks (owner, 16 Nov 2026): one SKU, added in the Add-on Sales tab, or
/// every stamp at once (<see cref="StampsKey"/>), which replaces the Daily Briefing's Stamp Count.
/// </summary>
public sealed record AddOnItem(string Key, string Name)
{
    public const string StampsKey = "STAMPS";

    public static AddOnItem Stamps { get; } = new(StampsKey, "Stamps");

    public bool IsStamps => string.Equals(Key, StampsKey, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One tracked add-on for the day: how many sold, for how much, and who sold the most.</summary>
public sealed record AddOnColumn(AddOnItem Item, int Quantity, decimal Value, string? TopSeller, int TopQuantity);

/// <summary>One person's add-ons, in the same order as the columns.</summary>
public sealed record AddOnPerson(string OperatorId, string Name, IReadOnlyList<int> Quantities, IReadOnlyList<decimal> Values)
{
    public int Total => Quantities.Sum();

    public decimal Value => Values.Sum();
}

/// <summary>
/// Add-on sales for the day or range, from Item Sales By Operator with every item in it (SKUs)
/// and its Stamps department (stamps, Christmas stamps left out as on the Stamp Count). People are
/// ordered by add-ons sold, most first; only people who sold at least one tracked add-on appear.
/// </summary>
public sealed record AddOnSalesSummary(IReadOnlyList<AddOnColumn> Columns, IReadOnlyList<AddOnPerson> People)
{
    public int TotalQuantity => Columns.Sum(c => c.Quantity);

    public decimal TotalValue => Columns.Sum(c => c.Value);

    /// <param name="tracked">The add-ons, in the order they were added.</param>
    /// <param name="allItems">Every item line from Item Sales By Operator (all product groups).</param>
    /// <param name="stampItems">The stamp lines; null when they were not read (Stamps then shows 0).</param>
    public static AddOnSalesSummary Build(
        IReadOnlyList<AddOnItem> tracked,
        IEnumerable<GiftCardItemRecord> allItems,
        IEnumerable<GiftCardItemRecord>? stampItems)
    {
        var items = allItems.ToList();
        var stamps = (stampItems ?? Array.Empty<GiftCardItemRecord>())
            .Where(s => !StampsSold.IsChristmas(s.Description))
            .ToList();

        // Each column's lines, then everyone who appears in any of them, in report order.
        var lines = tracked
            .Select(item => item.IsStamps ? stamps : items.Where(i => SameSku(i.ProductCode, item.Key)).ToList())
            .ToList();

        var people = new List<(string Id, string Name)>();
        foreach (var line in lines.SelectMany(l => l))
        {
            if (!people.Any(p => p.Id == line.OperatorId)) people.Add((line.OperatorId, line.OperatorName));
        }

        var rows = people
            .Select(p => new AddOnPerson(
                p.Id,
                p.Name,
                lines.Select(l => l.Where(i => i.OperatorId == p.Id).Sum(i => i.Quantity)).ToList(),
                lines.Select(l => l.Where(i => i.OperatorId == p.Id).Sum(i => i.Value)).ToList()))
            .Where(p => p.Quantities.Any(q => q != 0))
            .OrderByDescending(p => p.Total)
            .ThenByDescending(p => p.Value)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = tracked
            .Select((item, index) =>
            {
                var best = rows.Where(r => r.Quantities[index] > 0).MaxBy(r => r.Quantities[index]);
                return new AddOnColumn(
                    item,
                    lines[index].Sum(i => i.Quantity),
                    lines[index].Sum(i => i.Value),
                    best?.Name,
                    best?.Quantities[index] ?? 0);
            })
            .ToList();

        return new AddOnSalesSummary(columns, rows);
    }

    /// <summary>
    /// The <paramref name="count"/> add-ons that sold the most (ties: the one added first), kept
    /// in the order they were added, with each person's figures for those only - for the Daily
    /// Briefing, which has room for four (owner, 16 Nov 2026).
    /// </summary>
    public AddOnSalesSummary Top(int count)
    {
        var keep = Columns
            .Select((column, index) => (column, index))
            .OrderByDescending(c => c.column.Quantity)
            .ThenBy(c => c.index)
            .Take(Math.Max(0, count))
            .Select(c => c.index)
            .OrderBy(i => i)
            .ToList();

        var rows = People
            .Select(p => new AddOnPerson(p.OperatorId, p.Name,
                keep.Select(i => p.Quantities[i]).ToList(),
                keep.Select(i => p.Values[i]).ToList()))
            .Where(p => p.Quantities.Any(q => q != 0))
            .OrderByDescending(p => p.Total)
            .ThenByDescending(p => p.Value)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AddOnSalesSummary(keep.Select(i => Columns[i]).ToList(), rows);
    }

    /// <summary>The same SKU as typed: spaces and letter case ignored, and leading zeros too
    /// when both are numbers ("0031085" and "31085").</summary>
    public static bool SameSku(string? reported, string? typed)
    {
        var a = (reported ?? string.Empty).Trim();
        var b = (typed ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return a.All(char.IsDigit) && b.All(char.IsDigit)
            && string.Equals(a.TrimStart('0'), b.TrimStart('0'), StringComparison.Ordinal);
    }
}
