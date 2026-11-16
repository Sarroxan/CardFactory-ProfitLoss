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
}
