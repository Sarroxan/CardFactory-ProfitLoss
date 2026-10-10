namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <param name="DiscountTypeSelection">The report header's "Discount Type" criterion, e.g. "All".</param>
/// <param name="ReasonSelection">The report header's "Reason" criterion, e.g. "All".</param>
public sealed record DiscountReportParseResult(
    ParsedReportMetadata Metadata,
    string DiscountTypeSelection,
    string ReasonSelection,
    IReadOnlyList<DiscountLineRecord> Lines)
{
    public IReadOnlyList<DiscountLineRecord> StaffDiscountLines =>
        Lines.Where(line => line.IsStaffDiscount).ToArray();
}
