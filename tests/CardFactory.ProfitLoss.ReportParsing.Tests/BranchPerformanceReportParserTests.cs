using Xunit;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class BranchPerformanceReportParserTests
{
    [Fact]
    public void Parses_daily_branch_performance_and_preserves_report_metrics()
    {
        var html = DailyHtml();
        var result = new BranchPerformanceReportParser().Parse(html);

        Assert.Equal("0001 - Sample Store", result.Outlet);
        Assert.Equal(new DateTime(2026, 8, 26), result.FromDate);
        var day = Assert.Single(result.Days);
        Assert.Equal(2, day.Hours.Count);
        Assert.Equal(18, day.Total.Transactions);
        Assert.Equal(44, day.Total.Units);
        Assert.Equal(58.93m, day.Total.Sales);
        Assert.Equal(2.44m, day.Total.Aub);
        Assert.Equal(3.27m, day.Total.Abv);
        Assert.Equal(60.41m, day.Hours[0].PercentOfSales);
        Assert.Equal(12, day.Hours[0].Transactions);
        Assert.Equal(25, day.Hours[0].Units);
        Assert.Equal(35.60m, day.Hours[0].Sales);
    }

    [Fact]
    public void Parses_weekly_period_summary_with_comma_formatted_values()
    {
        var html = WeeklyHtml();
        var result = new BranchPerformanceReportParser().Parse(html);

        Assert.Equal(new DateTime(2026, 8, 17), result.FromDate);
        Assert.Equal(new DateTime(2026, 8, 23), result.ToDate);
        Assert.Equal(2, result.PeriodSummary.Count);
        Assert.Equal(1_849, result.PeriodTotal.Transactions);
        Assert.Equal(5_220, result.PeriodTotal.Units);
        Assert.Equal(8_369.01m, result.PeriodTotal.Sales);
        Assert.Equal(2.82m, result.PeriodTotal.Aub);
        Assert.Equal(4.53m, result.PeriodTotal.Abv);
        Assert.Equal(1_215.19m, result.PeriodSummary[1].Sales);
    }

    [Fact]
    public void Rejects_hourly_values_that_do_not_match_period_total()
    {
        var html = DailyHtml().Replace("<div class=\"totalBox\">58.93</div>", "<div class=\"totalBox\">99.99</div>", StringComparison.Ordinal);
        Assert.Throws<ReportParseException>(() => new BranchPerformanceReportParser().Parse(html));
    }

    [Fact]
    public void Parse_AllowsTitlelessGeneratedBodyWhenBranchStructureIsValid()
    {
        var html = DailyHtml();
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1\b[^>]*>.*?</h1>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<title\b[^>]*>.*?</title>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        var result = new BranchPerformanceReportParser().Parse(html);
        Assert.NotEmpty(result.Days);
    }

    [Fact]
    public void Parse_StillRejectsNonEmptyWrongBranchTitle()
    {
        var html = DailyHtml().Replace(BranchPerformanceReportParser.ExpectedTitle, "Different Report", StringComparison.Ordinal);
        var ex = Assert.Throws<ReportParseException>(() => new BranchPerformanceReportParser().Parse(html));
        Assert.Contains("Expected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string DailyHtml() => """
<html><head><title>Sales By Time Period Daily Report</title></head><body>
<h1>Sales By Time Period Daily Report</h1>
<table id="header_block"><tr><td>Outlet :</td><td>0001 - Sample Store</td></tr><tr><td>Date Range :</td><td>26/08/2026 - 26/08/2026</td></tr></table>
<table>
<tr><td class="sectionHead">DATE: 26/08/2026</td></tr>
<tr><td class="report">09:00 - 09:59</td><td>12</td><td>25</td><td>35.60</td><td>2.08</td><td>2.97</td><td>60.41</td></tr>
<tr><td class="report">10:00 - 10:59</td><td>6</td><td>19</td><td>23.33</td><td>3.17</td><td>3.89</td><td>39.59</td></tr>
<tr><td>DAILY TOTAL:</td><td><div class="totalBox">18</div></td><td><div class="totalBox">44</div></td><td><div class="totalBox">58.93</div></td><td><div class="totalBox">2.44</div></td><td><div class="totalBox">3.27</div></td><td><div class="totalBox">100.00</div></td></tr>
<tr><td class="sectionHead">PERIOD SUMMARY:</td></tr>
<tr><td class="report">09:00 - 09:59</td><td>12</td><td>25</td><td>35.60</td><td>2.08</td><td>2.97</td><td>60.41</td></tr>
<tr><td class="report">10:00 - 10:59</td><td>6</td><td>19</td><td>23.33</td><td>3.17</td><td>3.89</td><td>39.59</td></tr>
<tr><td>PERIOD TOTAL:</td><td><div class="totalBox">18</div></td><td><div class="totalBox">44</div></td><td><div class="totalBox">58.93</div></td><td><div class="totalBox">2.44</div></td><td><div class="totalBox">3.27</div></td><td><div class="totalBox">100.00</div></td></tr>
</table></body></html>
""";

    private static string WeeklyHtml() => """
<html><head><title>Sales By Time Period Daily Report</title></head><body>
<h1>Sales By Time Period Daily Report</h1>
<table id="header_block"><tr><td>Outlet :</td><td>0001 - Sample Store</td></tr><tr><td>Date Range :</td><td>17/08/2026 - 23/08/2026</td></tr></table>
<table>
<tr><td class="sectionHead">PERIOD SUMMARY:</td></tr>
<tr><td class="report">09:00 - 09:59</td><td>94</td><td>253</td><td>7,153.82</td><td>2.69</td><td>76.10</td><td>85.48</td></tr>
<tr><td class="report">13:00 - 13:59</td><td>1,755</td><td>4,967</td><td>1,215.19</td><td>2.83</td><td>0.69</td><td>14.52</td></tr>
<tr><td>PERIOD TOTAL:</td><td><div class="totalBox">1,849</div></td><td><div class="totalBox">5,220</div></td><td><div class="totalBox">8,369.01</div></td><td><div class="totalBox">2.82</div></td><td><div class="totalBox">4.53</div></td><td><div class="totalBox">100.00</div></td></tr>
</table></body></html>
""";

    [Fact]
    public void Parse_GeneratedDailyBody_AllowsMissingHeaderAndDerivesDateFromDateSection()
    {
        var html = DailyHtml();
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1\b[^>]*>.*?</h1>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<title\b[^>]*>.*?</title>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<table\s+id=""header_block""[^>]*>.*?</table>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        var result = new BranchPerformanceReportParser().Parse(html);

        Assert.Equal(string.Empty, result.Outlet);
        Assert.Equal(new DateTime(2026, 8, 26), result.FromDate);
        Assert.Equal(new DateTime(2026, 8, 26), result.ToDate);
        Assert.Single(result.Days);
    }

    [Fact]
    public void Parse_GeneratedWeeklyBody_AllowsMissingHeaderAndKeepsPeriodSummary()
    {
        var html = WeeklyHtml();
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1\b[^>]*>.*?</h1>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<title\b[^>]*>.*?</title>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<table\s+id=""header_block""[^>]*>.*?</table>", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        var result = new BranchPerformanceReportParser().Parse(html);

        Assert.Equal(DateTime.MinValue, result.FromDate);
        Assert.Equal(DateTime.MinValue, result.ToDate);
        Assert.Equal(2, result.PeriodSummary.Count);
        Assert.Equal(8_369.01m, result.PeriodTotal.Sales);
    }

}
