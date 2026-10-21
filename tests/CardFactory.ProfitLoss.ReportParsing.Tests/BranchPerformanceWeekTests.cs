using CardFactory.ProfitLoss.ReportParsing.Parsers;
using Xunit;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

/// <summary>The owner's real week (page 18, 14-20 Oct 2026, Report Type Daily, For Date),
/// store name replaced: one block per day, then the period. For the weekly Performance Sheet.</summary>
public sealed class BranchPerformanceWeekTests
{
    private const string Fixture = "BranchPerformanceWeek_2026-10-14_20.html";

    [Fact]
    public void RealWeek_ReadsEveryDayAndThePeriod()
    {
        var result = new BranchPerformanceReportParser().Parse(TestFixtureLoader.Read(Fixture));

        Assert.Equal(new DateTime(2026, 10, 14), result.FromDate);
        Assert.Equal(new DateTime(2026, 10, 20), result.ToDate);
        Assert.Equal(7, result.Days.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => new DateTime(2026, 10, 14).AddDays(i)), result.Days.Select(d => d.Date));

        var expected = new (int Transactions, int Units, decimal Sales, decimal Abv)[]
        {
            (181, 579, 938.81m, 5.19m), (259, 730, 1253.03m, 4.84m), (328, 953, 1774.15m, 5.41m),
            (339, 923, 1631.90m, 4.81m), (106, 302, 516.02m, 4.87m), (173, 498, 888.46m, 5.14m),
            (190, 665, 1139.46m, 6.00m),
        };
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(expected[i].Transactions, result.Days[i].Total.Transactions);
            Assert.Equal(expected[i].Units, result.Days[i].Total.Units);
            Assert.Equal(expected[i].Sales, result.Days[i].Total.Sales);
            Assert.Equal(expected[i].Abv, result.Days[i].Total.Abv);
        }

        Assert.Equal(1576, result.PeriodTotal.Transactions);
        Assert.Equal(8141.83m, result.PeriodTotal.Sales);
        Assert.Equal(result.PeriodTotal.Sales, result.Days.Sum(d => d.Total.Sales));
    }
}
