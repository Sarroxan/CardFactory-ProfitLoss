using CardFactory.ProfitLoss.Core.Models;
using CardFactory.ProfitLoss.Core.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class ProfitLossCalculatorTests
{
    private readonly ProfitLossCalculator _calculator = new();

    [Fact]
    public void Calculate_RemovesGiftCardValueAndQuantity_ButKeepsTransactions()
    {
        var input = new OperatorInput(
            "Dana Miller",
            Sales: 1250m,
            GiftCardValue: 50m,
            GiftCardQuantity: 2,
            Transactions: 100,
            Units: 180);

        var result = _calculator.Calculate(
            input,
            new PerformanceTargets(AbvTarget: 11.50m));

        Assert.Equal(1200m, result.CountedSales);
        Assert.Equal(178, result.CountedUnits);
        Assert.Equal(100, result.Transactions);
        Assert.Equal(12m, result.Abv);
        Assert.Equal(1.78m, result.Aub);
        Assert.Equal(50m, result.ProfitLoss);
    }

    [Fact]
    public void Calculate_ZeroTransactions_ReturnsZeroRatiosAndZeroProfitLoss()
    {
        var input = new OperatorInput(
            "Operator",
            Sales: 500m,
            GiftCardValue: 0m,
            GiftCardQuantity: 0,
            Transactions: 0,
            Units: 40);

        var result = _calculator.Calculate(
            input,
            new PerformanceTargets(AbvTarget: 10m));

        Assert.Equal(500m, result.CountedSales);
        Assert.Equal(40, result.CountedUnits);
        Assert.Equal(0m, result.Abv);
        Assert.Equal(0m, result.Aub);
        Assert.Equal(0m, result.ProfitLoss);
    }

    [Fact]
    public void Calculate_NoPositiveAbvTarget_ReturnsZeroProfitLoss()
    {
        var input = new OperatorInput(
            "Operator",
            Sales: 500m,
            GiftCardValue: 0m,
            GiftCardQuantity: 0,
            Transactions: 50,
            Units: 100);

        var result = _calculator.Calculate(input, PerformanceTargets.None);

        Assert.Equal(10m, result.Abv);
        Assert.Equal(2m, result.Aub);
        Assert.Equal(0m, result.ProfitLoss);
    }

    [Fact]
    public void Calculate_GiftCardsCannotMakeCountedValuesNegative()
    {
        var input = new OperatorInput(
            "Operator",
            Sales: 20m,
            GiftCardValue: 50m,
            GiftCardQuantity: 10,
            Transactions: 2,
            Units: 4);

        var result = _calculator.Calculate(
            input,
            new PerformanceTargets(AbvTarget: 10m));

        Assert.Equal(0m, result.CountedSales);
        Assert.Equal(0, result.CountedUnits);
        Assert.Equal(0m, result.Abv);
        Assert.Equal(0m, result.Aub);
        Assert.Equal(-20m, result.ProfitLoss);
    }

    [Fact]
    public void Calculate_NormalisesNegativeRawValuesToZero()
    {
        var input = new OperatorInput(
            " Operator ",
            Sales: -10m,
            GiftCardValue: -5m,
            GiftCardQuantity: -3,
            Transactions: -2,
            Units: -4);

        var result = _calculator.Calculate(
            input,
            new PerformanceTargets(AbvTarget: -1m));

        Assert.Equal("Operator", result.Name);
        Assert.Equal(0m, result.Sales);
        Assert.Equal(0m, result.GiftCardValue);
        Assert.Equal(0, result.GiftCardQuantity);
        Assert.Equal(0, result.Transactions);
        Assert.Equal(0, result.Units);
        Assert.Equal(0m, result.CountedSales);
        Assert.Equal(0, result.CountedUnits);
        Assert.Equal(0m, result.ProfitLoss);
    }

    [Fact]
    public void Calculate_CapsGiftCardQuantityAtCurrentV17Maximum()
    {
        var input = new OperatorInput(
            "Operator",
            Sales: 100m,
            GiftCardValue: 0m,
            GiftCardQuantity: 120,
            Transactions: 10,
            Units: 150);

        var result = _calculator.Calculate(input, PerformanceTargets.None);

        Assert.Equal(99, result.GiftCardQuantity);
        Assert.Equal(51, result.CountedUnits);
    }

    [Fact]
    public void Calculate_RepeatedCallsReturnTheSameResultAndDoNotMutateInput()
    {
        var input = new OperatorInput(
            "Operator",
            Sales: 100m,
            GiftCardValue: 25m,
            GiftCardQuantity: 1,
            Transactions: 5,
            Units: 10);
        var targets = new PerformanceTargets(AbvTarget: 12m);

        var first = _calculator.Calculate(input, targets);
        var second = _calculator.Calculate(input, targets);

        Assert.Equal(first, second);
        Assert.Equal(100m, input.Sales);
        Assert.Equal(25m, input.GiftCardValue);
        Assert.Equal(1, input.GiftCardQuantity);
        Assert.Equal(5, input.Transactions);
        Assert.Equal(10, input.Units);
    }
}
