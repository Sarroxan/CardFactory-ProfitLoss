namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>One person's share of an item in <see cref="ItemLookup"/>.</summary>
public sealed record ItemLookupSeller(string OperatorId, string OperatorName, int Quantity, decimal Value);

/// <summary>One item found by <see cref="ItemLookup"/>: every line for its SKU added together.</summary>
public sealed record ItemLookupMatch(
    string Code,
    string Name,
    string Department,
    int Quantity,
    decimal Value,
    IReadOnlyList<ItemLookupSeller> Sellers);

/// <summary>
/// Add-on Sales › Item lookup (owner, 26 Nov 2026, concept L1): find items in an all-items report by
/// SKU or by words in the name. Lines for the same SKU (one per person) are added together; the best
/// match comes first - the exact SKU, then names that start with what was typed, then most sold.
/// Only reads the report; no figure elsewhere is touched.
/// </summary>
public static class ItemLookup
{
    public static IReadOnlyList<ItemLookupMatch> Find(IEnumerable<GiftCardItemRecord> lines, string? query, int max = 10)
    {
        var typed = Normalise(query);
        if (typed.Length == 0) return Array.Empty<ItemLookupMatch>();
        var words = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var skuLike = words.Length == 1;

        var found = new List<(ItemLookupMatch Match, int Rank)>();
        foreach (var item in lines.Where(l => !string.IsNullOrWhiteSpace(l.ProductCode)).GroupBy(l => SkuKey(l.ProductCode)))
        {
            var code = item.First().ProductCode.Trim();
            var name = item.GroupBy(l => l.Description.Trim()).OrderByDescending(g => g.Count()).First().Key;
            var lowerName = Normalise(name);
            int rank;
            if (skuLike && AddOnSalesSummary.SameSku(code, typed)) rank = 0;
            else if (lowerName.StartsWith(typed, StringComparison.Ordinal)) rank = 1;
            else if (words.All(w => lowerName.Contains(w, StringComparison.Ordinal))) rank = 2;
            else if (skuLike && typed.Length >= 3 && code.Contains(typed, StringComparison.OrdinalIgnoreCase)) rank = 3;
            else continue;

            var sellers = item
                .GroupBy(l => (Id: l.OperatorId.Trim(), Name: l.OperatorName.Trim()))
                .Select(g => new ItemLookupSeller(g.Key.Id, g.Key.Name, g.Sum(l => l.Quantity), g.Sum(l => l.Value)))
                .OrderByDescending(s => s.Quantity).ThenByDescending(s => s.Value).ThenBy(s => s.OperatorName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var department = item.Select(l => l.Department).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? string.Empty;
            found.Add((new ItemLookupMatch(code, name, Readable(department), item.Sum(l => l.Quantity), item.Sum(l => l.Value), sellers), rank));
        }

        return found
            .OrderBy(f => f.Rank).ThenByDescending(f => f.Match.Quantity).ThenByDescending(f => f.Match.Value)
            .ThenBy(f => f.Match.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max).Select(f => f.Match).ToList();
    }

    /// <summary>"A/A/A/Gift Cards" → "A › Gift Cards": the report's path without repeats.</summary>
    public static string Readable(string department) =>
        string.Join(" › ", department.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string SkuKey(string code)
    {
        var c = code.Trim();
        return c.All(char.IsDigit) ? c.TrimStart('0') : c.ToUpperInvariant();
    }

    private static string Normalise(string? text) =>
        string.Join(' ', (text ?? string.Empty).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
