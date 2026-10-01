namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record RefundReportParseResult(
    ParsedReportMetadata Metadata,
    IReadOnlyList<RefundOperatorRecord> Operators);
