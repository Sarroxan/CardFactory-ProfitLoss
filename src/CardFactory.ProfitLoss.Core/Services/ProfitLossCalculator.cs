using CardFactory.ProfitLoss.Core.Models;

namespace CardFactory.ProfitLoss.Core.Services;

/// <summary>
/// Calculates one operator from immutable raw input. The service keeps no calculation state.
/// </summary>
public sealed class ProfitLossCalculator
{
    public const int MaximumGiftCardQuantity = 99;

    public OperatorPerformance Calculate(OperatorInput input, PerformanceTargets targets)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(targets);

        var name = input.Name?.Trim() ?? string.Empty;
        var sales = Math.Max(0m, input.Sales);
        var giftCardValue = Math.Max(0m, input.GiftCardValue);
        var giftCardQuantity = Math.Clamp(input.GiftCardQuantity, 0, MaximumGiftCardQuantity);
        var transactions = Math.Max(0, input.Transactions);
        var units = Math.Max(0, input.Units);

        var countedSales = Math.Max(0m, sales - giftCardValue);
        var countedUnits = Math.Max(0, units - giftCardQuantity);

        var abv = transactions > 0 ? countedSales / transactions : 0m;
        var aub = transactions > 0 ? (decimal)countedUnits / transactions : 0m;

        var abvTarget = Math.Max(0m, targets.AbvTarget);
        var profitLoss = abvTarget > 0m && transactions > 0
            ? countedSales - (abvTarget * transactions)
            : 0m;

        return new OperatorPerformance(
            name,
            sales,
            giftCardValue,
            giftCardQuantity,
            transactions,
            units,
            countedSales,
            countedUnits,
            abv,
            aub,
            profitLoss);
    }
}
