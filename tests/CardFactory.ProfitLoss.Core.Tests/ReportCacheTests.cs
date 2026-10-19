using CardFactory.ProfitLoss.Core.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class ReportCacheTests
{
    private static readonly DateTime Mon = new(2026, 10, 19);

    [Fact]
    public void PastDay_PulledAfterItEnded_IsUsedAgainAllDay()
    {
        var cache = new ReportCache();
        cache.Store("Refunds", Mon.AddDays(-1), Mon.AddDays(-1), "<html>sun</html>", Mon.AddHours(9));

        Assert.True(cache.TryGet("Refunds", Mon.AddDays(-1), Mon.AddDays(-1), Mon.AddHours(17), out var html, out var at));
        Assert.Equal("<html>sun</html>", html);
        Assert.Equal(Mon.AddHours(9), at);
    }

    [Fact]
    public void Today_IsUsedAgainFor10Minutes_ThenPulledFresh()
    {
        var cache = new ReportCache();
        var pulled = Mon.AddHours(15);
        cache.Store("Refunds", Mon, Mon, "x", pulled);

        Assert.True(cache.TryGet("Refunds", Mon, Mon, pulled.AddMinutes(10), out _, out _));
        Assert.False(cache.TryGet("Refunds", Mon, Mon, pulled.AddMinutes(10).AddSeconds(1), out _, out _));
    }

    [Fact]
    public void Today_PulledYesterdayEvening_IsNotTreatedAsFinished()
    {
        // Pulled during the day, the app left open overnight: the copy is part of a day.
        var cache = new ReportCache();
        cache.Store("Refunds", Mon, Mon, "x", Mon.AddHours(16));
        Assert.False(cache.TryGet("Refunds", Mon, Mon, Mon.AddDays(1).AddHours(9), out _, out _));
    }

    [Fact]
    public void DifferentReportOrPeriod_IsNotUsed()
    {
        var cache = new ReportCache();
        cache.Store("Refunds", Mon.AddDays(-1), Mon.AddDays(-1), "x", Mon.AddHours(9));

        Assert.False(cache.TryGet("Discounts", Mon.AddDays(-1), Mon.AddDays(-1), Mon.AddHours(10), out _, out _));
        Assert.False(cache.TryGet("Refunds", Mon.AddDays(-7), Mon.AddDays(-1), Mon.AddHours(10), out _, out _));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var cache = new ReportCache();
        cache.Store("Refunds", Mon.AddDays(-1), Mon.AddDays(-1), "x", Mon.AddHours(9));
        cache.Clear();
        Assert.False(cache.TryGet("Refunds", Mon.AddDays(-1), Mon.AddDays(-1), Mon.AddHours(10), out _, out _));
    }
}
