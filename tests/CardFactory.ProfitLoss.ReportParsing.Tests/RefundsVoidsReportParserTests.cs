using Xunit;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class RefundsVoidsReportParserTests
{
    private const string Fixture = "RefundsVoidsByOperator_2026-08-20.html";

    [Fact]
    public void Parse_GenuineReport_ReadsAllOperatorsAndExpectedFigures()
    {
        var result = new RefundsVoidsReportParser().Parse(TestFixtureLoader.Read(Fixture));

        Assert.Equal(3, result.Operators.Count);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Metadata.FromDate);
        Assert.Equal(new DateOnly(2026, 8, 20), result.Metadata.ToDate);
        Assert.Equal("All", result.Metadata.OperatorSelection);

        var dana = Assert.Single(result.Operators, item => item.OperatorId == "20000002");
        Assert.Equal("Dana Miller", dana.Name);
        Assert.Equal(79.46m, dana.Sales);
        Assert.Equal(13, dana.Transactions);
        Assert.Equal(42, dana.Units);

        var sam = Assert.Single(result.Operators, item => item.OperatorId == "100003");
        Assert.Equal("Sam Parker", sam.Name);
        Assert.Equal(162.40m, sam.Sales);
        Assert.Equal(42, sam.Transactions);
        Assert.Equal(132, sam.Units);

        var bailey = Assert.Single(result.Operators, item => item.OperatorId == "20000001");
        Assert.Equal("Bailey Rowe", bailey.Name);
        Assert.Equal(1157.57m, bailey.Sales);
        Assert.Equal(284, bailey.Transactions);
        Assert.Equal(731, bailey.Units);
    }

    [Fact]
    public void Parse_GenuineReport_DoesNotTreatGrandTotalsAsOperator()
    {
        var result = new RefundsVoidsReportParser().Parse(TestFixtureLoader.Read(Fixture));

        Assert.DoesNotContain(result.Operators, item => item.Name.Contains("Grand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_SameReportTwice_ReturnsIdenticalData()
    {
        var parser = new RefundsVoidsReportParser();
        var html = TestFixtureLoader.Read(Fixture);

        var first = parser.Parse(html);
        var second = parser.Parse(html);

        Assert.Equal(first.Metadata, second.Metadata);
        Assert.Equal(first.Operators, second.Operators);
    }

    [Fact]
    public void Parse_ItemSalesReport_RejectsWrongReportType()
    {
        var html = TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html");

        var error = Assert.Throws<ReportParseException>(() => new RefundsVoidsReportParser().Parse(html));

        Assert.Contains("Expected report", error.Message);
    }

    [Fact]
    public void Parse_MalformedSales_ThrowsClearError()
    {
        var html = TestFixtureLoader.Read(Fixture).Replace(">79.46<", ">not-a-number<", StringComparison.Ordinal);

        var error = Assert.Throws<ReportParseException>(() => new RefundsVoidsReportParser().Parse(html));

        Assert.Contains("Dana Miller sales", error.Message);
    }
    [Fact]
    public void Parse_LiveFlooidReportTitle_AcceptsReport()
    {
        var html = TestFixtureLoader.Read(Fixture)
            .Replace(RefundsVoidsReportParser.ExpectedTitle, "Refunds, Voids & No Sales Report", StringComparison.Ordinal);

        var result = new RefundsVoidsReportParser().Parse(html);

        Assert.NotEmpty(result.Operators);
    }

    [Fact]
    public void Parse_AllowsTitlelessGeneratedBodyWhenRefundStructureIsValid()
    {
        var html = TestFixtureLoader.Read(Fixture);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1\b[^>]*>.*?</h1>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<title\b[^>]*>.*?</title>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        var result = new RefundsVoidsReportParser().Parse(html);
        Assert.NotEmpty(result.Operators);
    }


    [Fact]
    public void Parse_GeneratedBody_AllowsMissingCommonSelectionMetadata()
    {
        var html = TestFixtureLoader.Read(Fixture);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1\b[^>]*>.*?</h1>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<title\b[^>]*>.*?</title>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (var label in new[] { "Outlet", "Operator", "Date Range", "Report Created" })
        {
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                $@"<tr\b[^>]*>(?:(?!</tr>).)*selectionHeaderTitle(?:(?!</tr>).)*{System.Text.RegularExpressions.Regex.Escape(label)}\s*:?(?:(?!</tr>).)*</tr>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        }

        var result = new RefundsVoidsReportParser().Parse(html);

        Assert.Equal(3, result.Operators.Count);
        Assert.Equal(string.Empty, result.Metadata.Outlet);
        Assert.Equal(DateOnly.MinValue, result.Metadata.FromDate);
        Assert.Equal(DateOnly.MinValue, result.Metadata.ToDate);
        Assert.Equal(DateTime.MinValue, result.Metadata.CreatedAt);
        Assert.Equal("All", result.Metadata.OperatorSelection);
    }

}
