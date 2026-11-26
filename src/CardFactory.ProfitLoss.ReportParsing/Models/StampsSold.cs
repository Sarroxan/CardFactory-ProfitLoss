namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// Stamps (owner, 21 Oct 2026): Christmas stamps were not counted - any line naming Christmas or
/// Xmas was skipped. Since 26 Nov 2026 (owner) the Stamps add-on counts them too, so nothing
/// uses this now; kept for its test. The 1st / 2nd / Large counts that
/// fed the Reports tile, the Daily Briefing's Stamp Count and the Team Scorecard tile went when
/// those were removed (owner, 16 Nov 2026).
/// </summary>
public static class StampsSold
{
    public static bool IsChristmas(string? description) =>
        (description ?? string.Empty).Contains("christmas", StringComparison.OrdinalIgnoreCase)
        || (description ?? string.Empty).Contains("xmas", StringComparison.OrdinalIgnoreCase);
}
