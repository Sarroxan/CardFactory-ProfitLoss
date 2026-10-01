using Xunit;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class GiftCardReportParserTests
{
    private const string Fixture = "ItemSalesGiftCards_2026-08-20.html";

    [Fact]
    public void Parse_GenuineReport_ReadsGiftCardOperatorTotal()
    {
        var result = new GiftCardReportParser().Parse(TestFixtureLoader.Read(Fixture));

        Assert.Equal("Gift Cards", result.ProductGroups);
        Assert.Contains("Show Item Detail", result.ReportType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Metadata.FromDate);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Metadata.ToDate);

        var bailey = Assert.Single(result.Operators);
        Assert.Equal("20000001", bailey.OperatorId);
        Assert.Equal("Bailey Rowe", bailey.Name);
        Assert.Equal(1, bailey.Quantity);
        Assert.Equal(20.00m, bailey.Value);

        var item = Assert.Single(result.Items);
        Assert.Equal("2127116", item.ProductCode);
        Assert.Equal("GC Love2Shop 20GBP.", item.Description);
        Assert.Equal("Bailey Rowe", item.OperatorName);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(20.00m, item.Value);
    }

    [Fact]
    public void Parse_SameReportTwice_ReturnsIdenticalData()
    {
        var parser = new GiftCardReportParser();
        var html = TestFixtureLoader.Read(Fixture);

        var first = parser.Parse(html);
        var second = parser.Parse(html);

        Assert.Equal(first.Metadata, second.Metadata);
        Assert.Equal(first.Operators, second.Operators);
    }

    [Fact]
    public void Parse_WrongProductGroup_IsRejected()
    {
        var html = TestFixtureLoader.Read(Fixture).Replace(
            "<td class=\"selectionHeaderDetail\" width=\"60%\">Gift Cards</td>",
            "<td class=\"selectionHeaderDetail\" width=\"60%\">Other</td>",
            StringComparison.Ordinal);

        var error = Assert.Throws<ReportParseException>(() => new GiftCardReportParser().Parse(html));

        Assert.Contains("Product Groups must be 'Gift Cards'", error.Message);
    }

    [Fact]
    public void Parse_MissingShowItemDetail_IsRejected()
    {
        var html = TestFixtureLoader.Read(Fixture).Replace("Show Item Detail", "Summary Only", StringComparison.Ordinal);

        var error = Assert.Throws<ReportParseException>(() => new GiftCardReportParser().Parse(html));

        Assert.Contains("Show Item Detail", error.Message);
    }

    [Fact]
    public void Parse_MalformedTotalValue_ThrowsClearError()
    {
        var html = TestFixtureLoader.Read(Fixture).Replace(
            "<td class=\"reportSubSummaryRight\" width=\"10%\">20.00</td>",
            "<td class=\"reportSubSummaryRight\" width=\"10%\">bad-value</td>",
            StringComparison.Ordinal);

        var error = Assert.Throws<ReportParseException>(() => new GiftCardReportParser().Parse(html));

        Assert.Contains("value", error.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Parse_AllowsTitlelessGeneratedBodyWhenGiftCardCriteriaAreValid()
    {
        var html = TestFixtureLoader.Read(Fixture);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1\b[^>]*>.*?</h1>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<title\b[^>]*>.*?</title>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        var result = new GiftCardReportParser().Parse(html);
        Assert.NotEmpty(result.Operators);
    }


    [Fact]
    public void Parse_GeneratedBody_AllowsMissingCommonMetadataButKeepsGiftCriteriaStrict()
    {
        var html = TestFixtureLoader.Read(Fixture);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1\b[^>]*>.*?</h1>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<title\b[^>]*>.*?</title>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (var label in new[] { "Outlet", "Operator", "Date Range", "Created on" })
        {
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                $@"<tr\b[^>]*>(?:(?!</tr>).)*selectionHeaderTitle(?:(?!</tr>).)*{System.Text.RegularExpressions.Regex.Escape(label)}\s*:?(?:(?!</tr>).)*</tr>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        }

        var result = new GiftCardReportParser().Parse(html);

        Assert.Single(result.Operators);
        Assert.Single(result.Items);
        Assert.Equal("Gift Cards", result.ProductGroups);
        Assert.Contains("Show Item Detail", result.ReportType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, result.Metadata.Outlet);
        Assert.Equal(DateOnly.MinValue, result.Metadata.FromDate);
    }

}
