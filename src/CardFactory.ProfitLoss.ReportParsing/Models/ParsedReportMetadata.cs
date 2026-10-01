namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// Common metadata captured from a completed Flooid report.
/// </summary>
public sealed record ParsedReportMetadata(
    string Title,
    string Outlet,
    DateOnly FromDate,
    DateOnly ToDate,
    DateTime CreatedAt,
    string OperatorSelection);
