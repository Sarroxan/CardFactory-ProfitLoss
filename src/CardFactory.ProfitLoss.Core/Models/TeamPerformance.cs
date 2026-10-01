namespace CardFactory.ProfitLoss.Core.Models;

/// <summary>
/// Calculated results for the complete operator collection.
/// </summary>
public sealed record TeamPerformance(
    IReadOnlyList<OperatorPerformance> Operators,
    decimal TotalCountedSales,
    int TotalTransactions,
    int TotalCountedUnits,
    decimal TotalProfitLoss,
    decimal TotalGiftCardValue,
    int TotalGiftCardQuantity,
    decimal ActualAbv,
    decimal ActualAub,
    decimal? SalesVariance,
    decimal? AbvVariance,
    decimal? AubVariance);
