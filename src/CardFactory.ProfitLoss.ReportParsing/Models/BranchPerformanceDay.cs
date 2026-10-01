namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record BranchPerformanceDay(
    DateTime Date,
    IReadOnlyList<BranchHourlyRecord> Hours,
    BranchPerformanceTotal Total);
