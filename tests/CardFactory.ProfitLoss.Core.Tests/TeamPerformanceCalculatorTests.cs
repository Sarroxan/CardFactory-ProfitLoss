using CardFactory.ProfitLoss.Core.Models;
using CardFactory.ProfitLoss.Core.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class TeamPerformanceCalculatorTests
{
    private readonly TeamPerformanceCalculator _calculator = new();

    [Fact]
    public void Calculate_UsesWeightedTeamAbvAndAub()
    {
        var operators = new[]
        {
            new OperatorInput(
                "Operator A",
                Sales: 100m,
                GiftCardValue: 0m,
                GiftCardQuantity: 0,
                Transactions: 10,
                Units: 20),
            new OperatorInput(
                "Operator B",
                Sales: 300m,
                GiftCardValue: 0m,
                GiftCardQuantity: 0,
                Transactions: 20,
                Units: 40)
        };

        var result = _calculator.Calculate(operators, PerformanceTargets.None);

        Assert.Equal(400m, result.TotalCountedSales);
        Assert.Equal(30, result.TotalTransactions);
        Assert.Equal(60, result.TotalCountedUnits);
        Assert.Equal(400m / 30m, result.ActualAbv);
        Assert.Equal(2m, result.ActualAub);
    }

    [Fact]
    public void Calculate_SumsGiftCardsAndProfitLossAcrossOperators()
    {
        var operators = new[]
        {
            new OperatorInput(
                "Operator A",
                Sales: 120m,
                GiftCardValue: 20m,
                GiftCardQuantity: 1,
                Transactions: 10,
                Units: 21),
            new OperatorInput(
                "Operator B",
                Sales: 250m,
                GiftCardValue: 50m,
                GiftCardQuantity: 2,
                Transactions: 20,
                Units: 42)
        };

        var result = _calculator.Calculate(
            operators,
            new PerformanceTargets(AbvTarget: 9m));

        Assert.Equal(300m, result.TotalCountedSales);
        Assert.Equal(70m, result.TotalGiftCardValue);
        Assert.Equal(3, result.TotalGiftCardQuantity);
        Assert.Equal(60, result.TotalCountedUnits);
        Assert.Equal(30m, result.TotalProfitLoss);
    }

    [Fact]
    public void Calculate_CalculatesTargetVariancesOnlyForPositiveTargets()
    {
        var operators = new[]
        {
            new OperatorInput(
                "Operator",
                Sales: 100m,
                GiftCardValue: 0m,
                GiftCardQuantity: 0,
                Transactions: 10,
                Units: 20)
        };

        var withTargets = _calculator.Calculate(
            operators,
            new PerformanceTargets(
                SalesTarget: 90m,
                AbvTarget: 9m,
                AubTarget: 1.5m));

        Assert.Equal(10m, withTargets.SalesVariance);
        Assert.Equal(1m, withTargets.AbvVariance);
        Assert.Equal(0.5m, withTargets.AubVariance);

        var withoutTargets = _calculator.Calculate(operators, PerformanceTargets.None);

        Assert.Null(withoutTargets.SalesVariance);
        Assert.Null(withoutTargets.AbvVariance);
        Assert.Null(withoutTargets.AubVariance);
    }

    [Fact]
    public void Calculate_EmptyTeamReturnsZeroTotals()
    {
        var result = _calculator.Calculate(
            Array.Empty<OperatorInput>(),
            PerformanceTargets.None);

        Assert.Empty(result.Operators);
        Assert.Equal(0m, result.TotalCountedSales);
        Assert.Equal(0, result.TotalTransactions);
        Assert.Equal(0, result.TotalCountedUnits);
        Assert.Equal(0m, result.TotalProfitLoss);
        Assert.Equal(0m, result.ActualAbv);
        Assert.Equal(0m, result.ActualAub);
    }

    [Fact]
    public void Calculate_RepeatedCallsReplaceCalculationStateRatherThanAccumulating()
    {
        var operators = new[]
        {
            new OperatorInput(
                "Operator A",
                Sales: 100m,
                GiftCardValue: 10m,
                GiftCardQuantity: 1,
                Transactions: 10,
                Units: 20),
            new OperatorInput(
                "Operator B",
                Sales: 200m,
                GiftCardValue: 20m,
                GiftCardQuantity: 2,
                Transactions: 20,
                Units: 40)
        };
        var targets = new PerformanceTargets(AbvTarget: 8m);

        var first = _calculator.Calculate(operators, targets);
        var second = _calculator.Calculate(operators, targets);

        Assert.Equal(first.TotalCountedSales, second.TotalCountedSales);
        Assert.Equal(first.TotalTransactions, second.TotalTransactions);
        Assert.Equal(first.TotalCountedUnits, second.TotalCountedUnits);
        Assert.Equal(first.TotalProfitLoss, second.TotalProfitLoss);
        Assert.Equal(first.TotalGiftCardValue, second.TotalGiftCardValue);
        Assert.Equal(first.TotalGiftCardQuantity, second.TotalGiftCardQuantity);
        Assert.Equal(first.ActualAbv, second.ActualAbv);
        Assert.Equal(first.ActualAub, second.ActualAub);
        Assert.True(first.Operators.SequenceEqual(second.Operators));

        Assert.Equal(100m, operators[0].Sales);
        Assert.Equal(200m, operators[1].Sales);
    }

    [Fact]
    public void Calculate_TeamRatiosUseTotalCountedValuesAndTotalTransactions()
    {
        var operators = new[]
        {
            new OperatorInput(
                "Operator With Transactions",
                Sales: 100m,
                GiftCardValue: 0m,
                GiftCardQuantity: 0,
                Transactions: 10,
                Units: 20),
            new OperatorInput(
                "Operator Missing Transactions",
                Sales: 50m,
                GiftCardValue: 0m,
                GiftCardQuantity: 0,
                Transactions: 0,
                Units: 10)
        };

        var result = _calculator.Calculate(operators, PerformanceTargets.None);

        Assert.Equal(150m, result.TotalCountedSales);
        Assert.Equal(10, result.TotalTransactions);
        Assert.Equal(30, result.TotalCountedUnits);
        Assert.Equal(15m, result.ActualAbv);
        Assert.Equal(3m, result.ActualAub);
    }
}
