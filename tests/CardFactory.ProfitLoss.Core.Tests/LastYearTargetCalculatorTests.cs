using CardFactory.ProfitLoss.Core.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class LastYearTargetCalculatorTests
{
    [Fact]
    public void LastYearRange_IsTheSameWeekday52WeeksBack()
    {
        var (from, to) = LastYearTargetCalculator.LastYearRange(new DateTime(2026, 10, 16), new DateTime(2026, 10, 16));

        Assert.Equal(new DateTime(2025, 10, 17), from);
        Assert.Equal(new DateTime(2025, 10, 17), to);
        Assert.Equal(DayOfWeek.Friday, from.DayOfWeek);
    }

    [Fact]
    public void LastYearRange_ShiftsAWholeWeek()
    {
        var (from, to) = LastYearTargetCalculator.LastYearRange(new DateTime(2026, 10, 12), new DateTime(2026, 10, 18));

        Assert.Equal(new DateTime(2025, 10, 13), from);
        Assert.Equal(new DateTime(2025, 10, 19), to);
        Assert.Equal(DayOfWeek.Monday, from.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, to.DayOfWeek);
    }

    [Fact]
    public void Calculate_AddsTheDefaultPercentages()
    {
        var result = LastYearTargetCalculator.Calculate(
            new DateTime(2025, 10, 17), new DateTime(2025, 10, 17), 240, 4.62m, 2.80m,
            LastYearTargetCalculator.DefaultAbvPercent, LastYearTargetCalculator.DefaultAubPercent);

        Assert.NotNull(result);
        Assert.Equal(4.62m, result!.LastYearAbv);
        Assert.Equal(5.08m, result.AbvTarget);    // 4.62 x 1.10 = 5.082
        Assert.Equal(2.80m, result.AubTarget);    // no change
    }

    [Fact]
    public void Calculate_RoundsLastYearFirstSoTheShownSumMatches()
    {
        // 4.6249 shows as £4.62; the target must be 4.62 x 1.10 = 5.08, not 4.6249 x 1.10 = 5.09.
        var result = LastYearTargetCalculator.Calculate(DateTime.Today, DateTime.Today, 100, 4.6249m, 2.8361m, 10m, 0m)!;

        Assert.Equal(4.62m, result.LastYearAbv);
        Assert.Equal(5.08m, result.AbvTarget);
        Assert.Equal(2.84m, result.AubTarget);
    }

    [Fact]
    public void Calculate_ReturnsNullWhenLastYearHadNoTransactions()
    {
        Assert.Null(LastYearTargetCalculator.Calculate(DateTime.Today, DateTime.Today, 0, 0m, 0m, 10m, 0m));
    }

    [Fact]
    public void Calculate_KeepsPercentagesWithinLimits()
    {
        var result = LastYearTargetCalculator.Calculate(DateTime.Today, DateTime.Today, 10, 5m, 3m, 500m, -90m)!;

        Assert.Equal(LastYearTargetCalculator.MaxPercent, result.AbvPercent);
        Assert.Equal(10m, result.AbvTarget);
        Assert.Equal(LastYearTargetCalculator.MinPercent, result.AubPercent);
        Assert.Equal(1.50m, result.AubTarget);
    }
}
