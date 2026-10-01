namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record GiftCardReportParseResult(
    ParsedReportMetadata Metadata,
    string ProductGroups,
    string ReportType,
    IReadOnlyList<GiftCardOperatorRecord> Operators)
{
    public IReadOnlyList<GiftCardItemRecord> Items { get; init; } = Array.Empty<GiftCardItemRecord>();
}
