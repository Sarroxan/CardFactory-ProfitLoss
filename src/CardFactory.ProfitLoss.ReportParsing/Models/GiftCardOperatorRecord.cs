namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// Gift Card totals read from Item Sales By Operator.
/// </summary>
public sealed record GiftCardOperatorRecord(
    string OperatorId,
    string Name,
    decimal Value,
    int Quantity);
