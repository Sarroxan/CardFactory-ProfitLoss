using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Internal;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.ReportParsing.Parsers;

/// <summary>Parses Flooid Branch Performance (backend title: Sales By Time Period Daily Report).</summary>
public sealed partial class BranchPerformanceReportParser
{
    public const string ExpectedTitle = "Sales By Time Period Daily Report";

    public BranchPerformanceReportParseResult Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) throw new ReportParseException("Branch Performance report HTML is empty.");

        var document = new HtmlParser().ParseDocument(html);
        var title = ReportText.NormalizeWhitespace(document.QuerySelector("h1")?.TextContent ?? document.Title);
        if (!string.IsNullOrWhiteSpace(title) && !title.Contains(ExpectedTitle, StringComparison.OrdinalIgnoreCase))
            throw new ReportParseException($"Expected '{ExpectedTitle}' but found '{title}'.");

        var fields = ReadHeaderFields(document);
        var outlet = fields.TryGetValue("Outlet", out var outletValue) && !string.IsNullOrWhiteSpace(outletValue)
            ? outletValue
            : string.Empty;
        var hasDateRange = fields.TryGetValue("Date Range", out var dateRange) && !string.IsNullOrWhiteSpace(dateRange);

        if (string.IsNullOrWhiteSpace(title))
        {
            var bodyText = ReportText.NormalizeWhitespace(document.Body?.TextContent);
            if (!bodyText.Contains("PERIOD TOTAL", StringComparison.OrdinalIgnoreCase))
                throw new ReportParseException($"Expected '{ExpectedTitle}' but found no report title and no Branch PERIOD TOTAL signature.");
        }
        var (fromDate, toDate) = hasDateRange
            ? ParseDateRange(dateRange!)
            : (DateTime.MinValue, DateTime.MinValue);

        var days = new List<BranchPerformanceDay>();
        var periodHours = new List<BranchHourlyRecord>();
        BranchPerformanceTotal? periodTotal = null;

        DateTime? currentDate = null;
        var currentHours = new List<BranchHourlyRecord>();

        foreach (var row in document.QuerySelectorAll("table tr"))
        {
            var directCells = row.Children.Where(c => c.LocalName is "td" or "th").ToArray();
            if (directCells.Length == 0) continue;
            var first = ReportText.NormalizeWhitespace(directCells[0].TextContent);

            if (directCells[0].ClassList.Contains("sectionHead"))
            {
                if (first.StartsWith("DATE:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentDate is not null && currentHours.Count > 0)
                        throw new ReportParseException($"{ExpectedTitle}: date section {currentDate:dd/MM/yyyy} is missing DAILY TOTAL.");
                    currentDate = ParseSectionDate(first);
                    currentHours = new List<BranchHourlyRecord>();
                }
                else if (first.StartsWith("PERIOD SUMMARY", StringComparison.OrdinalIgnoreCase))
                {
                    currentDate = null;
                }
                continue;
            }

            if (first.Equals("DAILY TOTAL:", StringComparison.OrdinalIgnoreCase))
            {
                if (currentDate is null) throw new ReportParseException($"{ExpectedTitle}: DAILY TOTAL found outside a DATE section.");
                var total = ParseTotal(directCells, "DAILY TOTAL");
                ValidateTotal(currentHours, total, $"{currentDate:dd/MM/yyyy}");
                days.Add(new BranchPerformanceDay(currentDate.Value, currentHours.AsReadOnly(), total));
                currentDate = null;
                currentHours = new List<BranchHourlyRecord>();
                continue;
            }

            if (first.Equals("PERIOD TOTAL:", StringComparison.OrdinalIgnoreCase))
            {
                periodTotal = ParseTotal(directCells, "PERIOD TOTAL");
                ValidateTotal(periodHours, periodTotal, "PERIOD SUMMARY");
                continue;
            }

            if (!TimeBandRegex().IsMatch(first) || directCells.Length < 7) continue;
            var hour = ParseHour(directCells);
            if (currentDate is not null) currentHours.Add(hour);
            else periodHours.Add(hour);
        }

        if (periodTotal is null) throw new ReportParseException($"{ExpectedTitle}: PERIOD TOTAL was not found.");
        if (days.Count == 0 && periodHours.Count == 0 && periodTotal.Sales != 0m)
            throw new ReportParseException($"{ExpectedTitle}: totals were present but no hourly rows were found.");

        if (!hasDateRange && days.Count > 0)
        {
            fromDate = days.Min(day => day.Date);
            toDate = days.Max(day => day.Date);
        }

        return new BranchPerformanceReportParseResult(
            outlet, fromDate, toDate, days.AsReadOnly(), periodHours.AsReadOnly(), periodTotal);
    }

    private static Dictionary<string, string> ReadHeaderFields(IDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.QuerySelectorAll("#header_block tr"))
        {
            var cells = row.Children.Where(c => c.LocalName is "td" or "th").ToArray();
            if (cells.Length < 2) continue;
            var key = ReportText.NormalizeWhitespace(cells[0].TextContent).TrimEnd(':').Trim();
            if (key.Length == 0) continue;
            result[key] = ReportText.NormalizeWhitespace(cells[1].TextContent);
        }
        return result;
    }

    private static string Require(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ReportParseException($"{ExpectedTitle}: header field '{key}' was not found.");

    private static (DateTime From, DateTime To) ParseDateRange(string value)
    {
        var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !TryDate(parts[0], out var from) || !TryDate(parts[1], out var to))
            throw new ReportParseException($"{ExpectedTitle}: invalid Date Range '{value}'.");
        return (from, to);
    }

    private static DateTime ParseSectionDate(string value)
    {
        var text = value[(value.IndexOf(':') + 1)..].Trim();
        if (!TryDate(text, out var date)) throw new ReportParseException($"{ExpectedTitle}: invalid DATE section '{value}'.");
        return date;
    }

    private static bool TryDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static BranchHourlyRecord ParseHour(IReadOnlyList<IElement> cells) => new(
        ReportText.NormalizeWhitespace(cells[0].TextContent),
        ReportText.ParseInt(cells[1].TextContent, "Customer Count"),
        ReportText.ParseInt(cells[2].TextContent, "Sales Quantity"),
        ReportText.ParseDecimal(cells[3].TextContent, "Sales Value"),
        ReportText.ParseDecimal(cells[4].TextContent, "Per Customer Quantity"),
        ReportText.ParseDecimal(cells[5].TextContent, "Per Customer Value"),
        ReportText.ParseDecimal(cells[6].TextContent, "Percentage Total Sales"));

    private static BranchPerformanceTotal ParseTotal(IReadOnlyList<IElement> cells, string context)
    {
        if (cells.Count < 7) throw new ReportParseException($"{ExpectedTitle}: {context} has too few columns.");
        return new BranchPerformanceTotal(
            ReportText.ParseInt(cells[1].TextContent, $"{context} transactions"),
            ReportText.ParseInt(cells[2].TextContent, $"{context} units"),
            ReportText.ParseDecimal(cells[3].TextContent, $"{context} sales"),
            ReportText.ParseDecimal(cells[4].TextContent, $"{context} AUB"),
            ReportText.ParseDecimal(cells[5].TextContent, $"{context} ABV"),
            ReportText.ParseDecimal(cells[6].TextContent, $"{context} percentage"));
    }

    private static void ValidateTotal(IReadOnlyCollection<BranchHourlyRecord> hours, BranchPerformanceTotal total, string context)
    {
        if (hours.Sum(h => h.Transactions) != total.Transactions ||
            hours.Sum(h => h.Units) != total.Units ||
            hours.Sum(h => h.Sales) != total.Sales)
        {
            throw new ReportParseException($"{ExpectedTitle}: {context} hourly values do not match the report total.");
        }
    }

    [GeneratedRegex(@"^\d{2}:\d{2}\s*-\s*\d{2}:\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeBandRegex();
}
