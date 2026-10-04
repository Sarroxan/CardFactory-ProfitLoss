namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record RefundReportParseResult(
    ParsedReportMetadata Metadata,
    IReadOnlyList<RefundOperatorRecord> Operators)
{
    /// <summary>
    /// Voids, refunds and no-sales per operator, for the Reports section. Empty when the
    /// report's column headings are not the ones these figures were mapped from - the Team
    /// figures above never depend on it.
    /// </summary>
    public IReadOnlyList<OperatorActivityRecord> Activity { get; init; } = Array.Empty<OperatorActivityRecord>();
}
