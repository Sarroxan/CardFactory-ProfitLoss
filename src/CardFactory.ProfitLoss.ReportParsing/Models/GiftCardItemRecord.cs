namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record GiftCardItemRecord(
    string OperatorId,
    string OperatorName,
    string ProductCode,
    string Description,
    int Quantity,
    decimal Value);
