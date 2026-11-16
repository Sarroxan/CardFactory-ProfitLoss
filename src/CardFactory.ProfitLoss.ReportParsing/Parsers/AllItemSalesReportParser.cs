using AngleSharp.Html.Parser;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Internal;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.ReportParsing.Parsers;

/// <summary>
/// Item Sales By Operator with every item in it - Product Group and Product left blank - for
/// Add-on Sales (owner, 16 Nov 2026). The owner's save (page 19) shows the same item lines as the
/// Gift Cards report (code, description, quantity, value, average), with department headings and
/// department totals between them, so the Gift Cards reading is used as it is, checked for
/// "Product Groups: All". "Product" must say All too: a code left in "Products to Include"
/// would narrow the report to that one item.
/// </summary>
public sealed class AllItemSalesReportParser
{
    public const string AllProductGroups = "All";

    public GiftCardReportParseResult Parse(string html)
    {
        var result = new GiftCardReportParser(AllProductGroups).Parse(html);

        var fields = ReportText.ReadSelectionFields(new HtmlParser().ParseDocument(html));
        if (fields.TryGetValue("Product", out var product)
            && !string.Equals(ReportText.NormalizeWhitespace(product), "All", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportParseException(
                $"{GiftCardReportParser.ExpectedTitle}: Product must be 'All' for add-on sales but was '{ReportText.NormalizeWhitespace(product)}'.");
        }

        return result;
    }

    /// <summary>The Gift Cards department, levels 1-4 of the gift card product group
    /// 200005 &gt; 300017 &gt; 400065 &gt; 500303, exactly as the Gift Cards report prints it (saved
    /// page 10 and its fixture). 500303 holds one group only, 600527 Gift Cards (saved page 09),
    /// so its lines are the Gift Cards report's lines.</summary>
    public const string GiftCardsDepartment = "Stamps & Carrier Bags/Stamps & Carrier Bags/Stamps & Carrier Bags/Gift Cards";

    /// <summary>The Stamps department, as the Stamps report prints it (saved page 15); 500300
    /// holds one group only, 600561 Stamps (saved page 17).</summary>
    public const string StampsDepartment = "Stamps & Carrier Bags/Stamps & Carrier Bags/Stamps & Carrier Bags/Stamps";

    private static bool InDepartment(GiftCardItemRecord item, string department) =>
        string.Equals(ReportText.NormalizeWhitespace(item.Department), department, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Speed (owner, 16 Nov 2026: "do all the speed fixes"): the Gift Cards figures read from the
    /// all-items report, so the five-level product group picker (about 6.4 s) is not needed. The
    /// result is what the Gift Cards report itself gives: its lines, and per person the sum of
    /// them, in report order, for the same store and dates.
    /// </summary>
    public static GiftCardReportParseResult GiftCards(GiftCardReportParseResult allItems)
    {
        var lines = allItems.Items.Where(i => InDepartment(i, GiftCardsDepartment)).ToList();
        var operators = lines
            .GroupBy(i => i.OperatorId)
            .Select(g => new GiftCardOperatorRecord(g.Key, g.First().OperatorName, g.Sum(i => i.Value), g.Sum(i => i.Quantity)))
            .ToList();
        return new GiftCardReportParseResult(allItems.Metadata, "Gift Cards", allItems.ReportType, operators.AsReadOnly())
        {
            Items = lines.AsReadOnly()
        };
    }

    /// <summary>The lines the Stamps report would give, read from the all-items report (no separate pull).</summary>
    public static IReadOnlyList<GiftCardItemRecord> Stamps(GiftCardReportParseResult allItems) =>
        allItems.Items.Where(i => InDepartment(i, StampsDepartment)).ToList().AsReadOnly();
}
