using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.ReportParsing.Internal;

internal static partial class ReportText
{
    private static readonly CultureInfo BritishCulture = CultureInfo.GetCultureInfo("en-GB");

    public static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    public static string NormalizeLabel(string? value) =>
        NormalizeWhitespace(value).TrimEnd(':').Trim();

    public static string NormalizeOutlet(string? value) =>
        OutletHyphenRegex().Replace(NormalizeWhitespace(value), "-").ToUpperInvariant();

    public static string NormalizeOperatorName(string? value)
    {
        var normalized = NormalizeWhitespace(value).Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    public static (string OperatorId, string Name) ParseOperatorIdentity(string value, string context)
    {
        var normalized = NormalizeWhitespace(value);
        var match = OperatorIdentityRegex().Match(normalized);

        if (!match.Success)
        {
            throw new ReportParseException($"{context}: expected an operator in the form 'ID - Name' but found '{normalized}'.");
        }

        var operatorId = match.Groups["id"].Value;
        var name = NormalizeWhitespace(match.Groups["name"].Value);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ReportParseException($"{context}: operator name is blank.");
        }

        return (operatorId, name);
    }

    public static decimal ParseDecimal(string value, string context)
    {
        var normalized = NormalizeWhitespace(value);
        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, BritishCulture, out var result))
        {
            throw new ReportParseException($"{context}: '{normalized}' is not a valid number.");
        }

        return result;
    }

    public static int ParseInt(string value, string context)
    {
        var normalized = NormalizeWhitespace(value);
        if (!int.TryParse(normalized, NumberStyles.Integer | NumberStyles.AllowThousands, BritishCulture, out var result))
        {
            throw new ReportParseException($"{context}: '{normalized}' is not a valid whole number.");
        }

        return result;
    }

    public static int ParseQuantity(string value, string context)
    {
        var quantity = ParseDecimal(value, context);
        if (quantity != decimal.Truncate(quantity) || quantity < int.MinValue || quantity > int.MaxValue)
        {
            throw new ReportParseException($"{context}: '{NormalizeWhitespace(value)}' is not a valid whole-number quantity.");
        }

        return (int)quantity;
    }

    public static DateTime ParseTimestamp(string value, string context)
    {
        var normalized = NormalizeWhitespace(value);
        if (!DateTime.TryParseExact(
                normalized,
                "dd/MM/yyyy HH:mm:ss",
                BritishCulture,
                DateTimeStyles.None,
                out var result))
        {
            throw new ReportParseException($"{context}: expected dd/MM/yyyy HH:mm:ss but found '{normalized}'.");
        }

        return result;
    }

    public static (DateOnly FromDate, DateOnly ToDate) ParseDateRange(string value, string context)
    {
        var normalized = NormalizeWhitespace(value);
        var match = DateRangeRegex().Match(normalized);

        if (!match.Success ||
            !DateOnly.TryParseExact(match.Groups["from"].Value, "dd/MM/yyyy", BritishCulture, DateTimeStyles.None, out var fromDate) ||
            !DateOnly.TryParseExact(match.Groups["to"].Value, "dd/MM/yyyy", BritishCulture, DateTimeStyles.None, out var toDate))
        {
            throw new ReportParseException($"{context}: expected 'dd/MM/yyyy - dd/MM/yyyy' but found '{normalized}'.");
        }

        if (toDate < fromDate)
        {
            throw new ReportParseException($"{context}: To date cannot be earlier than From date.");
        }

        return (fromDate, toDate);
    }

    public static IReadOnlyDictionary<string, string> ReadSelectionFields(IDocument document)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var titleCell in document.QuerySelectorAll("td.selectionHeaderTitle"))
        {
            var label = NormalizeLabel(titleCell.TextContent);
            var detailCell = titleCell.NextElementSibling;

            if (string.IsNullOrWhiteSpace(label) || detailCell is null)
            {
                continue;
            }

            var value = NormalizeWhitespace(detailCell.TextContent);
            if (!fields.ContainsKey(label))
            {
                fields.Add(label, value);
            }
        }

        return fields;
    }

    private static string? OptionalField(
        IReadOnlyDictionary<string, string> fields,
        params string[] acceptedLabels)
    {
        foreach (var label in acceptedLabels)
        {
            if (fields.TryGetValue(label, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    public static string RequireField(
        IReadOnlyDictionary<string, string> fields,
        string reportName,
        params string[] acceptedLabels)
    {
        foreach (var label in acceptedLabels)
        {
            if (fields.TryGetValue(label, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new ReportParseException(
            $"{reportName}: required report field '{string.Join("' or '", acceptedLabels)}' was not found.");
    }

    public static ParsedReportMetadata BuildMetadata(
        IDocument document,
        string expectedTitle,
        params string[] createdAtLabels) =>
        BuildMetadataCore(document, expectedTitle, allowMissingTitle: false, allowMissingCommonFields: false, createdAtLabels);

    public static ParsedReportMetadata BuildMetadataAllowingMissingTitle(
        IDocument document,
        string expectedTitle,
        params string[] createdAtLabels) =>
        BuildMetadataCore(document, expectedTitle, allowMissingTitle: true, allowMissingCommonFields: false, createdAtLabels);

    public static ParsedReportMetadata BuildGeneratedResponseMetadata(
        IDocument document,
        string expectedTitle,
        params string[] createdAtLabels) =>
        BuildMetadataCore(document, expectedTitle, allowMissingTitle: true, allowMissingCommonFields: true, createdAtLabels);

    private static ParsedReportMetadata BuildMetadataCore(
        IDocument document,
        string expectedTitle,
        bool allowMissingTitle,
        bool allowMissingCommonFields,
        params string[] createdAtLabels)
    {
        var heading = NormalizeWhitespace(document.QuerySelector("h1")?.TextContent);
        if (!string.Equals(heading, expectedTitle, StringComparison.OrdinalIgnoreCase)
            && !(allowMissingTitle && string.IsNullOrWhiteSpace(heading)))
        {
            throw new ReportParseException(
                $"Expected report '{expectedTitle}' but found '{(string.IsNullOrWhiteSpace(heading) ? "no report title" : heading)}'.");
        }

        var fields = ReadSelectionFields(document);

        var outlet = allowMissingCommonFields
            ? OptionalField(fields, "Outlet") ?? string.Empty
            : RequireField(fields, expectedTitle, "Outlet");

        var operatorSelection = allowMissingCommonFields
            ? OptionalField(fields, "Operator") ?? "All"
            : RequireField(fields, expectedTitle, "Operator");

        if (!string.Equals(NormalizeWhitespace(operatorSelection), "All", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportParseException(
                $"{expectedTitle}: Operator must be 'All' but was '{NormalizeWhitespace(operatorSelection)}'.");
        }

        var dateRangeText = allowMissingCommonFields
            ? OptionalField(fields, "Date Range")
            : RequireField(fields, expectedTitle, "Date Range");
        var (fromDate, toDate) = string.IsNullOrWhiteSpace(dateRangeText)
            ? (DateOnly.MinValue, DateOnly.MinValue)
            : ParseDateRange(dateRangeText, $"{expectedTitle} Date Range");

        var createdAtText = allowMissingCommonFields
            ? OptionalField(fields, createdAtLabels)
            : RequireField(fields, expectedTitle, createdAtLabels);
        var createdAt = string.IsNullOrWhiteSpace(createdAtText)
            ? DateTime.MinValue
            : ParseTimestamp(createdAtText, $"{expectedTitle} creation time");

        return new ParsedReportMetadata(
            expectedTitle,
            outlet,
            fromDate,
            toDate,
            createdAt,
            operatorSelection);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s*-\s*")]
    private static partial Regex OutletHyphenRegex();

    [GeneratedRegex(@"^(?<id>\d+)\s*-\s*(?<name>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex OperatorIdentityRegex();

    [GeneratedRegex(@"^(?<from>\d{2}/\d{2}/\d{4})\s*-\s*(?<to>\d{2}/\d{2}/\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex DateRangeRegex();
}
