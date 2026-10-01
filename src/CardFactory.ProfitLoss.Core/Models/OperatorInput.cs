namespace CardFactory.ProfitLoss.Core.Models;

/// <summary>
/// Raw values for one operator before any Profit & Loss calculations are applied.
/// </summary>
public sealed record OperatorInput(
    string Name,
    decimal Sales,
    decimal GiftCardValue,
    int GiftCardQuantity,
    int Transactions,
    int Units);
