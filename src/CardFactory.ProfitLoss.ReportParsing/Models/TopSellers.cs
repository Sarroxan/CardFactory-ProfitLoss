using CardFactory.ProfitLoss.ReportParsing.Internal;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>The buttons above the Top sellers list (owner, 17 Nov 2026, concept T1).</summary>
public enum TopSellerGroup
{
    All,
    Cards,
    PartyAndGifting,
    WrapAndBags,
    StampsAndOther
}

/// <summary>One item over the period: every person's lines added together.</summary>
public sealed record TopSeller(string Code, string Name, int Quantity, decimal Value, TopSellerGroup Group);

/// <summary>
/// Top sellers (owner, 17 Nov 2026, concept T1): every item in Item Sales By Operator with every
/// item, for the last 4 weeks, added up across people and ranked. Gift cards are left out, as
/// everywhere in the app (they don't count towards KPIs), and so are carrier bags (owner: "Leave
/// them out" - they'd always be top). Groups come from the department path the report prints
/// above each item (saved page 19): level 1 "Everyday Card" / "Seasonal Card" are Cards; level 1
/// "... Party &amp; Gifting" is Party &amp; gifting, except level 3 "Wrap &amp; Bags"; level 1
/// "Stamps &amp; Carrier Bags" (stamps, helium, services, charity - saved page 16) is Stamps &amp;
/// other. Anything else shows under All only. Figures aren't touched by any of this.
/// </summary>
public static class TopSellers
{
    /// <summary>Carrier bags' department (saved page 19), left out of Top sellers.</summary>
    public const string CarrierBagsDepartment = "Stamps & Carrier Bags/Stamps & Carrier Bags/Stamps & Carrier Bags/Carrier Bags";

    /// <summary>How many are listed.</summary>
    public const int ListLength = 25;

    public static TopSellerGroup GroupOf(string? department)
    {
        var levels = ReportText.NormalizeWhitespace(department).Split('/', StringSplitOptions.TrimEntries);
        var top = levels.Length > 0 ? levels[0] : string.Empty;
        if (top.EndsWith(" Card", StringComparison.OrdinalIgnoreCase)) return TopSellerGroup.Cards;
        if (top.EndsWith("Party & Gifting", StringComparison.OrdinalIgnoreCase))
            return levels.Length > 2 && string.Equals(levels[2], "Wrap & Bags", StringComparison.OrdinalIgnoreCase)
                ? TopSellerGroup.WrapAndBags
                : TopSellerGroup.PartyAndGifting;
        if (string.Equals(top, "Stamps & Carrier Bags", StringComparison.OrdinalIgnoreCase)) return TopSellerGroup.StampsAndOther;
        return TopSellerGroup.All;
    }

    private static bool LeftOut(GiftCardItemRecord item)
    {
        var department = ReportText.NormalizeWhitespace(item.Department);
        return string.Equals(department, AllItemSalesReportParser.GiftCardsDepartment, StringComparison.OrdinalIgnoreCase)
            || string.Equals(department, CarrierBagsDepartment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every item, added up across people (same SKU = same item, as for add-ons), most sold first.</summary>
    public static IReadOnlyList<TopSeller> Build(IEnumerable<GiftCardItemRecord> items)
    {
        var totals = new List<TopSeller>();
        foreach (var line in items)
        {
            var code = (line.ProductCode ?? string.Empty).Trim();
            if (code.Length == 0 || LeftOut(line)) continue;
            var at = totals.FindIndex(t => AddOnSalesSummary.SameSku(t.Code, code));
            var name = (line.Description ?? string.Empty).Trim();
            if (at < 0)
            {
                totals.Add(new TopSeller(code, name.Length > 0 ? name : code, line.Quantity, line.Value, GroupOf(line.Department)));
            }
            else
            {
                var known = totals[at];
                totals[at] = known with { Quantity = known.Quantity + line.Quantity, Value = known.Value + line.Value };
            }
        }

        return totals.Where(t => t.Quantity > 0)
            .OrderByDescending(t => t.Quantity).ThenByDescending(t => t.Value).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The list on screen: one group (or all), by number sold or by £, at most <see cref="ListLength"/>.</summary>
    public static IReadOnlyList<TopSeller> Top(IEnumerable<TopSeller> all, TopSellerGroup group, bool byValue, int take = ListLength)
    {
        var chosen = all.Where(t => group == TopSellerGroup.All || t.Group == group);
        var ordered = byValue
            ? chosen.OrderByDescending(t => t.Value).ThenByDescending(t => t.Quantity)
            : chosen.OrderByDescending(t => t.Quantity).ThenByDescending(t => t.Value);
        return ordered.ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Take(take).ToList();
    }
}
