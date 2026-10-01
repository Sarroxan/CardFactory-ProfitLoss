using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Internal;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.ReportParsing.Parsers;

/// <summary>
/// Strict parser for Item Sales By Operator when configured for Gift Cards.
/// </summary>
public sealed class GiftCardReportParser
{
    public const string ExpectedTitle = "Item Sales By Operator Report";

    public GiftCardReportParseResult Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ReportParseException("Gift Card report HTML is empty.");
        }

        var document = new HtmlParser().ParseDocument(html);
        var fields = ReportText.ReadSelectionFields(document);
        var productGroups = ReportText.RequireField(fields, ExpectedTitle, "Product Groups");
        var reportType = ReportText.RequireField(fields, ExpectedTitle, "Report Type");

        if (!string.Equals(ReportText.NormalizeWhitespace(productGroups), "Gift Cards", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportParseException(
                $"{ExpectedTitle}: Product Groups must be 'Gift Cards' but was '{ReportText.NormalizeWhitespace(productGroups)}'.");
        }

        if (!reportType.Contains("Show Item Detail", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportParseException(
                $"{ExpectedTitle}: Report Type must include 'Show Item Detail' but was '{ReportText.NormalizeWhitespace(reportType)}'.");
        }

        // Product Groups=Gift Cards and Show Item Detail have already been validated above.
        // Permit an absent h1 at this point; a non-empty incorrect h1 remains an error.
        var metadata = ReportText.BuildGeneratedResponseMetadata(document, ExpectedTitle, "Created on");

        var operators = ParseOperatorTotals(document, out var grandTotal);
        ValidateGrandTotal(operators, grandTotal);
        var items = ParseItemDetails(document);

        return new GiftCardReportParseResult(
            metadata,
            productGroups,
            reportType,
            operators.AsReadOnly())
        {
            Items = items.AsReadOnly()
        };
    }


    private static List<GiftCardItemRecord> ParseItemDetails(IDocument document)
    {
        var items = new List<GiftCardItemRecord>();
        string currentOperatorId = string.Empty;
        string currentOperatorName = string.Empty;

        foreach (var cell in document.QuerySelectorAll("td"))
        {
            var text = ReportText.NormalizeWhitespace(cell.TextContent);
            if (cell.ClassList.Contains("reportSubHead"))
            {
                if (LooksLikeOperatorIdentity(text))
                {
                    (currentOperatorId, currentOperatorName) = ReportText.ParseOperatorIdentity(text, "Gift Card item operator");
                }
                else if (text.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Grand Total", StringComparison.OrdinalIgnoreCase))
                {
                    currentOperatorId = string.Empty;
                    currentOperatorName = string.Empty;
                }
            }

            if (!cell.ClassList.Contains("report") || string.IsNullOrWhiteSpace(currentOperatorId)) continue;
            var row = cell.ParentElement;
            if (row is null || row.LocalName != "tr") continue;
            var direct = row.Children.Where(c => c.LocalName is "td" or "th").ToArray();
            if (direct.Length != 5 || !ReferenceEquals(direct[0], cell)) continue;
            if (!direct[1].ClassList.Contains("report") || !direct[2].ClassList.Contains("reportRight") ||
                !direct[3].ClassList.Contains("reportRight")) continue;

            var code = ReportText.NormalizeWhitespace(direct[0].TextContent);
            var description = ReportText.NormalizeWhitespace(direct[1].TextContent);
            var quantity = ReportText.ParseQuantity(direct[2].TextContent, $"{currentOperatorName} {description} quantity");
            var value = ReportText.ParseDecimal(direct[3].TextContent, $"{currentOperatorName} {description} value");
            items.Add(new GiftCardItemRecord(currentOperatorId, currentOperatorName, code, description, quantity, value));
        }

        return items;
    }

    private static List<GiftCardOperatorRecord> ParseOperatorTotals(
        IDocument document,
        out GiftCardGrandTotal grandTotal)
    {
        var operators = new List<GiftCardOperatorRecord>();
        PendingOperator? currentOperator = null;
        GiftCardGrandTotal? parsedGrandTotal = null;

        foreach (var headingCell in document.QuerySelectorAll("td.reportSubHead"))
        {
            var heading = ReportText.NormalizeWhitespace(headingCell.TextContent);

            if (heading.Equals("Grand Total", StringComparison.OrdinalIgnoreCase))
            {
                if (currentOperator is not null)
                {
                    throw new ReportParseException(
                        $"{ExpectedTitle}: operator '{currentOperator.Name}' did not have a matching total row before Grand Total.");
                }

                if (parsedGrandTotal is not null)
                {
                    throw new ReportParseException($"{ExpectedTitle}: more than one Grand Total row was found.");
                }

                var (quantity, value) = ReadSummaryValues(headingCell, "Grand Total");
                parsedGrandTotal = new GiftCardGrandTotal(value, quantity);
                continue;
            }

            if (heading.StartsWith("Total ", StringComparison.OrdinalIgnoreCase))
            {
                if (currentOperator is null)
                {
                    throw new ReportParseException($"{ExpectedTitle}: found '{heading}' without a preceding operator section.");
                }

                var totalName = ReportText.NormalizeWhitespace(heading["Total ".Length..]);
                if (!string.Equals(
                        ReportText.NormalizeOperatorName(totalName),
                        ReportText.NormalizeOperatorName(currentOperator.Name),
                        StringComparison.Ordinal))
                {
                    throw new ReportParseException(
                        $"{ExpectedTitle}: total row '{heading}' does not match current operator '{currentOperator.Name}'.");
                }

                var (quantity, value) = ReadSummaryValues(headingCell, heading);
                if (operators.Any(item => item.OperatorId == currentOperator.OperatorId))
                {
                    throw new ReportParseException(
                        $"{ExpectedTitle}: duplicate operator ID '{currentOperator.OperatorId}' was found.");
                }

                operators.Add(new GiftCardOperatorRecord(
                    currentOperator.OperatorId,
                    currentOperator.Name,
                    value,
                    quantity));

                currentOperator = null;
                continue;
            }

            if (!LooksLikeOperatorIdentity(heading))
            {
                continue;
            }

            if (currentOperator is not null)
            {
                throw new ReportParseException(
                    $"{ExpectedTitle}: operator '{currentOperator.Name}' did not have a matching total row before the next operator section.");
            }

            var (operatorId, name) = ReportText.ParseOperatorIdentity(heading, "Gift Card report operator");
            currentOperator = new PendingOperator(operatorId, name);
        }

        if (currentOperator is not null)
        {
            throw new ReportParseException(
                $"{ExpectedTitle}: operator '{currentOperator.Name}' did not have a matching total row.");
        }

        if (parsedGrandTotal is null)
        {
            throw new ReportParseException($"{ExpectedTitle}: Grand Total row was not found.");
        }

        grandTotal = parsedGrandTotal;
        return operators;
    }

    private static bool LooksLikeOperatorIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hyphenIndex = value.IndexOf('-');
        if (hyphenIndex <= 0)
        {
            return false;
        }

        return value[..hyphenIndex].Trim().All(char.IsDigit);
    }

    private static (int Quantity, decimal Value) ReadSummaryValues(IElement headingCell, string context)
    {
        var table = FindNearestAncestorTable(headingCell)
            ?? throw new ReportParseException($"{ExpectedTitle}: could not locate the table for '{context}'.");

        var summaryCells = table.QuerySelectorAll("td.reportSubSummaryRight")
            .Select(cell => ReportText.NormalizeWhitespace(cell.TextContent))
            .ToArray();

        if (summaryCells.Length != 3)
        {
            throw new ReportParseException(
                $"{ExpectedTitle}: '{context}' expected Quantity, Value and Avg. Value but found {summaryCells.Length} summary values.");
        }

        return (
            ReportText.ParseQuantity(summaryCells[0], $"{context} quantity"),
            ReportText.ParseDecimal(summaryCells[1], $"{context} value"));
    }

    private static IElement? FindNearestAncestorTable(IElement element)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (current.LocalName.Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }
        }

        return null;
    }

    private static void ValidateGrandTotal(
        IReadOnlyCollection<GiftCardOperatorRecord> operators,
        GiftCardGrandTotal grandTotal)
    {
        var calculatedValue = operators.Sum(item => item.Value);
        var calculatedQuantity = operators.Sum(item => item.Quantity);

        if (calculatedValue != grandTotal.Value || calculatedQuantity != grandTotal.Quantity)
        {
            throw new ReportParseException(
                $"{ExpectedTitle}: operator Gift Card totals do not match Grand Total. " +
                $"Calculated Value={calculatedValue:0.00}, Quantity={calculatedQuantity}; " +
                $"Report Value={grandTotal.Value:0.00}, Quantity={grandTotal.Quantity}.");
        }
    }

    private sealed record PendingOperator(string OperatorId, string Name);

    private sealed record GiftCardGrandTotal(decimal Value, int Quantity);
}
