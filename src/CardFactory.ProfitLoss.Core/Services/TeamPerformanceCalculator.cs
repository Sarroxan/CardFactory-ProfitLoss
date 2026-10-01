using CardFactory.ProfitLoss.Core.Models;

namespace CardFactory.ProfitLoss.Core.Services;

/// <summary>
/// Calculates a complete team from raw operator inputs. Every call builds a new result and never
/// adds to values from an earlier calculation.
/// </summary>
public sealed class TeamPerformanceCalculator
{
    private readonly ProfitLossCalculator _operatorCalculator;

    public TeamPerformanceCalculator(ProfitLossCalculator? operatorCalculator = null)
    {
        _operatorCalculator = operatorCalculator ?? new ProfitLossCalculator();
    }

    public TeamPerformance Calculate(
        IEnumerable<OperatorInput> inputs,
        PerformanceTargets targets)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(targets);

        var operators = inputs
            .Select(input => _operatorCalculator.Calculate(input, targets))
            .ToArray();

        var totalCountedSales = operators.Sum(item => item.CountedSales);
        var totalTransactions = operators.Sum(item => item.Transactions);
        var totalCountedUnits = operators.Sum(item => item.CountedUnits);
        var totalProfitLoss = operators.Sum(item => item.ProfitLoss);
        var totalGiftCardValue = operators.Sum(item => item.GiftCardValue);
        var totalGiftCardQuantity = operators.Sum(item => item.GiftCardQuantity);

        // Approved rule: team ABV/AUB use weighted totals, never an average of operator ratios.
        var actualAbv = totalTransactions > 0
            ? totalCountedSales / totalTransactions
            : 0m;

        var actualAub = totalTransactions > 0
            ? (decimal)totalCountedUnits / totalTransactions
            : 0m;

        var salesTarget = Math.Max(0m, targets.SalesTarget);
        var abvTarget = Math.Max(0m, targets.AbvTarget);
        var aubTarget = Math.Max(0m, targets.AubTarget);

        decimal? salesVariance = salesTarget > 0m
            ? totalCountedSales - salesTarget
            : null;

        decimal? abvVariance = abvTarget > 0m
            ? actualAbv - abvTarget
            : null;

        decimal? aubVariance = aubTarget > 0m
            ? actualAub - aubTarget
            : null;

        return new TeamPerformance(
            operators,
            totalCountedSales,
            totalTransactions,
            totalCountedUnits,
            totalProfitLoss,
            totalGiftCardValue,
            totalGiftCardQuantity,
            actualAbv,
            actualAub,
            salesVariance,
            abvVariance,
            aubVariance);
    }
}
