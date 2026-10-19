namespace CardFactory.ProfitLoss.Core.Services;

/// <summary>
/// Reports already pulled from Flooid this run (owner, 19 Oct 2026, option 1). Signing in,
/// then opening the Daily Briefing or Reports, used to pull the same reports again: nine pulls
/// where four would do. Each report is kept with the day or range it covers and when it was
/// pulled, in memory only (nothing is kept once the app closes).
///
/// A copy is used again when:
/// - it was pulled after the period ended - a finished day can't change; or
/// - it was pulled in the last 10 minutes - today's figures move while the shop trades.
/// Refresh skips this and always pulls fresh.
/// </summary>
public sealed class ReportCache
{
    public static readonly TimeSpan TodayKeepFor = TimeSpan.FromMinutes(10);

    private readonly Dictionary<(string Report, DateTime From, DateTime To), (string Html, DateTime PulledAt)> _held = new();

    public bool TryGet(string report, DateTime from, DateTime to, DateTime now, out string html, out DateTime pulledAt)
    {
        if (_held.TryGetValue((report, from.Date, to.Date), out var held) && StillGood(to, held.PulledAt, now))
        {
            html = held.Html;
            pulledAt = held.PulledAt;
            return true;
        }
        html = string.Empty;
        pulledAt = default;
        return false;
    }

    /// <param name="pulledAt">When the pull started, so a copy is never counted as newer than it is.</param>
    public void Store(string report, DateTime from, DateTime to, string html, DateTime pulledAt) =>
        _held[(report, from.Date, to.Date)] = (html, pulledAt);

    /// <summary>A new sign in may be another store: nothing held may carry over.</summary>
    public void Clear() => _held.Clear();

    public static bool StillGood(DateTime periodEnd, DateTime pulledAt, DateTime now)
    {
        if (pulledAt > now) return false;                        // clock moved back: don't trust it
        if (pulledAt.Date > periodEnd.Date) return true;         // pulled after the period was over
        return now - pulledAt <= TodayKeepFor;
    }
}
