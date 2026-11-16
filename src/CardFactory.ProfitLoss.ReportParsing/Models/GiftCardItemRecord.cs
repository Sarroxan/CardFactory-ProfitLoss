namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record GiftCardItemRecord(
    string OperatorId,
    string OperatorName,
    string ProductCode,
    string Description,
    int Quantity,
    decimal Value)
{
    /// <summary>The department heading the line sits under, e.g. "Stamps &amp; Carrier Bags/Stamps
    /// &amp; Carrier Bags/Stamps &amp; Carrier Bags/Gift Cards" (the report's product group path,
    /// levels 1-4). Empty when the report had none.</summary>
    public string Department { get; init; } = string.Empty;
}
