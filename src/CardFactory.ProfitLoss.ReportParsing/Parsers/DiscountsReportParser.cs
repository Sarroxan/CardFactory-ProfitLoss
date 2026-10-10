using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Internal;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.ReportParsing.Parsers;

/// <summary>
/// Strict parser for "Discounts &amp; Price Overrides" (Report Output: Detailed), as saved from
/// the live system on 10/10/2026. The data is <c>table#detailReportTable</c>: a group header
/// row, a column header row, one <c>tr.report</c> per discounted line, spacer rows, and a
/// <c>tr.reportSummaryRight</c> REPORT TOTAL row.
/// </summary>
public sealed class DiscountsReportParser
{
    public const string ExpectedTitle = "Discounts & Price Overrides";

    private static readonly CultureInfo BritishCulture = CultureInfo.GetCultureInfo("en-GB");

    private static readonly string[] RequiredColumns =
    {
        "Product Code", "Selling Code", "Description", "Discount Type", "Reason", "Date",
        "Operator", "Quantity", "Selling Price", "Discount Price", "Discount Value", "Discount % Applied"
    };

    public DiscountReportParseResult Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new ReportParseException("Discounts report HTML is empty.");

        var document = new HtmlParser().ParseDocument(html);
        var metadata = ReportText.BuildMetadata(document, ExpectedTitle, "Created on");
        var fields = ReportText.ReadSelectionFields(document);
        var discountType = ReportText.RequireField(fields, ExpectedTitle, "Discount Type");
        var reason = ReportText.RequireField(fields, ExpectedTitle, "Reason");

        var output = ReportText.RequireField(fields, ExpectedTitle, "Report Output");
        if (!output.Equals("Detailed", StringComparison.OrdinalIgnoreCase))
            throw new ReportParseException($"{ExpectedTitle}: Report Output must be 'Detailed' but was '{output}'.");

        var table = document.QuerySelector("table#detailReportTable");
        if (table is null)
        {
            // Not yet seen on the live system: a day with no discounts at all. Accept it as
            // empty only when the page is otherwise a finished report, so a truncated or
            // wrong page still fails.
            if (document.Body?.TextContent.Contains("End Of Report", StringComparison.OrdinalIgnoreCase) == true)
                return new DiscountReportParseResult(metadata, discountType, reason, Array.Empty<DiscountLineRecord>());
            throw new ReportParseException($"{ExpectedTitle}: report data table 'detailReportTable' was not found.");
        }

        var rows = table.QuerySelectorAll("tr").ToArray();
        var headerRow = rows.FirstOrDefault(row => DirectCells(row)
                .Any(cell => Text(cell).Equals("Product Code", StringComparison.OrdinalIgnoreCase)))
            ?? throw new ReportParseException($"{ExpectedTitle}: the column header row ('Product Code' ...) was not found.");
        var headers = DirectCells(headerRow).Select(Text).ToArray();
        var column = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredColumns)
        {
            var index = Array.FindIndex(headers, header => header.Equals(required, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new ReportParseException($"{ExpectedTitle}: required column '{required}' was not found.");
            column[required] = index;
        }

        var lines = new List<DiscountLineRecord>();
        decimal[]? totals = null;
        foreach (var row in rows)
        {
            if (row.ClassList.Contains("reportSummaryRight"))
            {
                totals = ReadTotals(row);
                continue;
            }
            if (!row.ClassList.Contains("report")) continue;

            var cells = DirectCells(row);
            if (cells.Count != headers.Length)
                throw new ReportParseException(
                    $"{ExpectedTitle}: a discount line has {cells.Count} cells; the header row has {headers.Length}.");

            string Cell(string name) => Text(cells[column[name]]);
            var description = Cell("Description");
            var context = $"{ExpectedTitle} line '{description}'";
            var dateText = Cell("Date");
            if (!DateOnly.TryParseExact(dateText, "dd/MM/yyyy", BritishCulture, DateTimeStyles.None, out var date))
                throw new ReportParseException($"{context}: date '{dateText}' is not dd/MM/yyyy.");
            var operatorId = Cell("Operator");
            if (operatorId.Length == 0 || !operatorId.All(char.IsDigit))
                throw new ReportParseException($"{context}: operator '{operatorId}' is not an operator code.");

            lines.Add(new DiscountLineRecord(
                Cell("Product Code"),
                Cell("Selling Code"),
                description,
                Cell("Discount Type"),
                Cell("Reason"),
                date,
                operatorId,
                ReportText.ParseDecimal(Cell("Quantity"), context + " quantity"),
                ReportText.ParseDecimal(Cell("Selling Price"), context + " selling price"),
                ReportText.ParseDecimal(Cell("Discount Price"), context + " discount price"),
                ReportText.ParseDecimal(Cell("Discount Value"), context + " discount value"),
                ReportText.ParseDecimal(Cell("Discount % Applied"), context + " discount %")));
        }

        if (totals is null)
            throw new ReportParseException($"{ExpectedTitle}: REPORT TOTAL row was not found.");

        var calculated = new[]
        {
            lines.Sum(line => line.Quantity),
            lines.Sum(line => line.SellingPrice),
            lines.Sum(line => line.DiscountPrice),
            lines.Sum(line => line.DiscountValue)
        };
        if (!calculated.SequenceEqual(totals))
            throw new ReportParseException(
                $"{ExpectedTitle}: the lines do not add up to the REPORT TOTAL row. " +
                $"Calculated Quantity={calculated[0]:0.00}, Selling={calculated[1]:0.00}, Discount Price={calculated[2]:0.00}, Discount Value={calculated[3]:0.00}; " +
                $"Report Quantity={totals[0]:0.00}, Selling={totals[1]:0.00}, Discount Price={totals[2]:0.00}, Discount Value={totals[3]:0.00}.");

        return new DiscountReportParseResult(metadata, discountType, reason, lines.AsReadOnly());
    }

    /// <summary>
    /// The REPORT TOTAL row does not line up cell for cell with the data rows (its first cell
    /// spans seven columns), so its figures are read in order: Quantity, Selling Price,
    /// Discount Price, Discount Value - the four numeric cells it carries on the saved report.
    /// </summary>
    private static decimal[] ReadTotals(IElement row)
    {
        var cells = DirectCells(row);
        if (cells.Count == 0 || !Text(cells[0]).Equals("REPORT TOTAL", StringComparison.OrdinalIgnoreCase))
            throw new ReportParseException($"{ExpectedTitle}: a summary row was found that is not REPORT TOTAL ('{(cells.Count == 0 ? "" : Text(cells[0]))}').");

        var numbers = cells.Skip(1).Select(Text).Where(text => text.Length > 0).ToArray();
        if (numbers.Length != 4)
            throw new ReportParseException($"{ExpectedTitle}: REPORT TOTAL has {numbers.Length} figures; 4 were expected.");
        return numbers.Select(text => ReportText.ParseDecimal(text, $"{ExpectedTitle} REPORT TOTAL")).ToArray();
    }

    private static string Text(IElement cell) => ReportText.NormalizeWhitespace(cell.TextContent);

    private static IReadOnlyList<IElement> DirectCells(IElement row) =>
        row.Children.Where(child => child.LocalName is "td" or "th").ToArray();
}
