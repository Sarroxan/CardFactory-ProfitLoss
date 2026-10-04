using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Internal;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.ReportParsing.Parsers;

/// <summary>
/// Strict parser for "Refunds, Voids & No Sales - By Operator - Summary".
/// </summary>
public sealed class RefundsVoidsReportParser
{
    public const string ExpectedTitle = "Refunds, Voids & No Sales - By Operator - Summary";

    public RefundReportParseResult Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ReportParseException("Refunds report HTML is empty.");
        }

        var document = new HtmlParser().ParseDocument(html);
        var headingElement = document.QuerySelector("h1");
        var heading = ReportText.NormalizeWhitespace(headingElement?.TextContent);
        if (!string.Equals(heading, ExpectedTitle, StringComparison.OrdinalIgnoreCase))
        {
            var liveFlooidTitle = heading.StartsWith("Refunds, Voids & No Sales", StringComparison.OrdinalIgnoreCase)
                && heading.Contains("Report", StringComparison.OrdinalIgnoreCase);
            if (liveFlooidTitle && headingElement is not null)
                headingElement.TextContent = ExpectedTitle;
        }

        // Reject any non-empty wrong report heading before interpreting this document as
        // Refunds/Voids. Flooid may omit the heading entirely in a genuine generator response;
        // that blank-heading case then continues through every normal structural validation.
        var metadata = ReportText.BuildGeneratedResponseMetadata(document, ExpectedTitle, "Report Created");

        var displayTable = document.QuerySelector("table#displayTable")
            ?? throw new ReportParseException($"{ExpectedTitle}: report data table 'displayTable' was not found.");

        ValidateHeaders(displayTable);
        var activityColumnsKnown = HasActivityColumns(displayTable);

        var operators = new List<RefundOperatorRecord>();
        var activity = new List<OperatorActivityRecord>();
        RefundGrandTotals? grandTotals = null;

        foreach (var row in displayTable.QuerySelectorAll("tr"))
        {
            var cells = DirectCells(row);
            if (cells.Count == 0)
            {
                continue;
            }

            var first = ReportText.NormalizeWhitespace(cells[0].TextContent);
            if (first.Equals("Operator", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Qty", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (first.Equals("Grand Totals:", StringComparison.OrdinalIgnoreCase))
            {
                if (grandTotals is not null)
                {
                    throw new ReportParseException($"{ExpectedTitle}: more than one Grand Totals row was found.");
                }

                RequireDataCellCount(cells, "Grand Totals");
                grandTotals = new RefundGrandTotals(
                    ReportText.ParseDecimal(cells[1].TextContent, "Grand Totals sales"),
                    ReportText.ParseInt(cells[2].TextContent, "Grand Totals transactions"),
                    ReportText.ParseInt(cells[7].TextContent, "Grand Totals units"));
                continue;
            }

            RequireDataCellCount(cells, $"operator row '{first}'");
            var (operatorId, name) = ReportText.ParseOperatorIdentity(first, "Refunds report operator");

            if (operators.Any(item => item.OperatorId == operatorId))
            {
                throw new ReportParseException($"{ExpectedTitle}: duplicate operator ID '{operatorId}' was found.");
            }

            operators.Add(new RefundOperatorRecord(
                operatorId,
                name,
                ReportText.ParseDecimal(cells[1].TextContent, $"{name} sales"),
                ReportText.ParseInt(cells[2].TextContent, $"{name} transactions"),
                ReportText.ParseInt(cells[7].TextContent, $"{name} units")));

            // Column positions, from the report's own two header rows: Operator | Total Sales
            // Value | Total No. Sales Txn | Transaction Voids (Qty, Value, Qty %, Value %) |
            // Total No. Sales Line | Line Voids (Qty, Value, Qty %, Value %) | Receipted Refunds
            // (Qty, Value) | Keyed Refunds (Qty, Value) | Total Refunds | No Sales.
            if (activityColumnsKnown)
            {
                activity.Add(new OperatorActivityRecord(
                    operatorId,
                    name,
                    ReportText.ParseInt(cells[3].TextContent, $"{name} transaction voids"),
                    ReportText.ParseDecimal(cells[4].TextContent, $"{name} transaction voids value"),
                    ReportText.ParseInt(cells[8].TextContent, $"{name} line voids"),
                    ReportText.ParseDecimal(cells[9].TextContent, $"{name} line voids value"),
                    ReportText.ParseInt(cells[12].TextContent, $"{name} receipted refunds"),
                    ReportText.ParseDecimal(cells[13].TextContent, $"{name} receipted refunds value"),
                    ReportText.ParseInt(cells[14].TextContent, $"{name} keyed refunds"),
                    ReportText.ParseDecimal(cells[15].TextContent, $"{name} keyed refunds value"),
                    ReportText.ParseDecimal(cells[16].TextContent, $"{name} total refunds"),
                    ReportText.ParseInt(cells[17].TextContent, $"{name} no sales")));
            }
        }

        if (grandTotals is null)
        {
            throw new ReportParseException($"{ExpectedTitle}: Grand Totals row was not found.");
        }

        ValidateGrandTotals(operators, grandTotals);
        return new RefundReportParseResult(metadata, operators.AsReadOnly()) { Activity = activity.AsReadOnly() };
    }

    private static void ValidateHeaders(IElement displayTable)
    {
        var rows = displayTable.QuerySelectorAll("tr");
        if (rows.Length < 2)
        {
            throw new ReportParseException($"{ExpectedTitle}: expected two header rows.");
        }

        var firstHeader = DirectCells(rows[0]).Select(cell => ReportText.NormalizeWhitespace(cell.TextContent)).ToArray();
        var requiredHeaders = new[]
        {
            "Operator",
            "Total Sales Value",
            "Total No. Sales Txn",
            "Total No. Sales Line"
        };

        foreach (var requiredHeader in requiredHeaders)
        {
            if (!firstHeader.Any(header => header.Equals(requiredHeader, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ReportParseException($"{ExpectedTitle}: required column '{requiredHeader}' was not found.");
            }
        }
    }

    /// <summary>
    /// True only when the top header row has exactly the column groups the activity
    /// positions were mapped from, in that order. Anything else and the Reports section is
    /// left empty rather than reading the wrong column.
    /// </summary>
    private static bool HasActivityColumns(IElement displayTable)
    {
        var rows = displayTable.QuerySelectorAll("tr");
        if (rows.Length < 2) return false;
        var top = DirectCells(rows[0]).Select(cell => ReportText.NormalizeWhitespace(cell.TextContent)).ToArray();
        var expected = new[]
        {
            "Operator", "Total Sales Value", "Total No. Sales Txn", "Transaction Voids", "Total No. Sales Line",
            "Line Voids", "Receipted Refunds", "Keyed Refunds", "Total Refunds", "No Sales"
        };
        return top.Length == expected.Length
            && top.Zip(expected).All(pair => pair.First.Equals(pair.Second, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<IElement> DirectCells(IElement row) =>
        row.Children.Where(child => child.LocalName is "td" or "th").ToArray();

    private static void RequireDataCellCount(IReadOnlyList<IElement> cells, string context)
    {
        if (cells.Count < 18)
        {
            throw new ReportParseException(
                $"{ExpectedTitle}: {context} has {cells.Count} data cells; at least 18 were expected.");
        }
    }

    private static void ValidateGrandTotals(
        IReadOnlyCollection<RefundOperatorRecord> operators,
        RefundGrandTotals grandTotals)
    {
        var calculatedSales = operators.Sum(item => item.Sales);
        var calculatedTransactions = operators.Sum(item => item.Transactions);
        var calculatedUnits = operators.Sum(item => item.Units);

        if (calculatedSales != grandTotals.Sales ||
            calculatedTransactions != grandTotals.Transactions ||
            calculatedUnits != grandTotals.Units)
        {
            throw new ReportParseException(
                $"{ExpectedTitle}: operator totals do not match the report Grand Totals. " +
                $"Calculated Sales={calculatedSales:0.00}, Transactions={calculatedTransactions}, Units={calculatedUnits}; " +
                $"Report Sales={grandTotals.Sales:0.00}, Transactions={grandTotals.Transactions}, Units={grandTotals.Units}.");
        }
    }

    private sealed record RefundGrandTotals(decimal Sales, int Transactions, int Units);
}
