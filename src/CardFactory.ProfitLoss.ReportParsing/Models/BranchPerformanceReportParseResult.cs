namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record BranchPerformanceReportParseResult(
    string Outlet,
    DateTime FromDate,
    DateTime ToDate,
    IReadOnlyList<BranchPerformanceDay> Days,
    IReadOnlyList<BranchHourlyRecord> PeriodSummary,
    BranchPerformanceTotal PeriodTotal);
