namespace CardFactory.ProfitLoss.Core.Models;

/// <summary>
/// Normalised raw values plus the calculated result for one operator.
/// </summary>
public sealed record OperatorPerformance(
    string Name,
    decimal Sales,
    decimal GiftCardValue,
    int GiftCardQuantity,
    int Transactions,
    int Units,
    decimal CountedSales,
    int CountedUnits,
    decimal Abv,
    decimal Aub,
    decimal ProfitLoss);
