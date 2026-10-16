namespace CardFactory.ProfitLoss.Core.Services;

/// <summary>
/// Targets from last year (owner, 16 Oct 2026): ABV and AUB targets are last year's figure for
/// the same trading period plus a percentage (defaults ABV +10%, AUB +0%).
///
/// "Last year" is the same weekday 52 weeks back (364 days), so Fri 16 Oct 2026 compares with
/// Fri 17 Oct 2025 and a Mon-Sun week with last year's Mon-Sun week. Last year's ABV and AUB
/// are worked out exactly as the app's own Team figures are (gift cards taken out, see
/// TeamPerformanceCalculator); this class only shifts the dates and applies the percentages.
/// </summary>
public static class LastYearTargetCalculator
{
    public const int DaysBack = 364;
    public const decimal MinPercent = -50m;
    public const decimal MaxPercent = 100m;
    public const decimal DefaultAbvPercent = 10m;
    public const decimal DefaultAubPercent = 0m;

    /// <summary>The same weekdays 52 weeks earlier.</summary>
    public static (DateTime From, DateTime To) LastYearRange(DateTime from, DateTime to) =>
        (from.Date.AddDays(-DaysBack), to.Date.AddDays(-DaysBack));

    /// <summary>
    /// Last year's ABV and AUB are rounded to 2 places first, so the sum shown to the user
    /// ("£4.62 + 10% = £5.08") is exactly the sum the target came from. Null when last year
    /// had no transactions (closed, or no trading in that period).
    /// </summary>
    public static LastYearTargets? Calculate(
        DateTime lastYearFrom,
        DateTime lastYearTo,
        int lastYearTransactions,
        decimal lastYearAbv,
        decimal lastYearAub,
        decimal abvPercent,
        decimal aubPercent)
    {
        if (lastYearTransactions <= 0) return null;

        abvPercent = Math.Clamp(abvPercent, MinPercent, MaxPercent);
        aubPercent = Math.Clamp(aubPercent, MinPercent, MaxPercent);
        var abv = Round(Math.Max(0m, lastYearAbv));
        var aub = Round(Math.Max(0m, lastYearAub));

        return new LastYearTargets(
            lastYearFrom.Date,
            lastYearTo.Date,
            abv,
            aub,
            abvPercent,
            aubPercent,
            Round(abv * (1m + abvPercent / 100m)),
            Round(aub * (1m + aubPercent / 100m)));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record LastYearTargets(
    DateTime FromDate,
    DateTime ToDate,
    decimal LastYearAbv,
    decimal LastYearAub,
    decimal AbvPercent,
    decimal AubPercent,
    decimal AbvTarget,
    decimal AubTarget);
