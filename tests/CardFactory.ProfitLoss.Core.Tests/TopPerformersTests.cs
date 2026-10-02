using CardFactory.ProfitLoss.Core.Models;
using CardFactory.ProfitLoss.Core.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class TopPerformersTests
{
    private static OperatorPerformance Person(string name, int transactions, decimal abv, decimal aub, decimal profitLoss) =>
        new(name, abv * transactions, 0m, 0, transactions, (int)(aub * transactions), abv * transactions, (int)(aub * transactions), abv, aub, profitLoss);

    [Fact]
    public void Find_NamesTheBestForEachMeasure()
    {
        var team = new[]
        {
            Person("Dana Miller", 28, 6.29m, 3.89m, 26.04m),
            Person("Riley Shaw", 57, 4.97m, 2.90m, -22.23m),
            Person("Bailey Rowe", 66, 5.90m, 3.17m, 35.64m),
            Person("Sam Parker", 20, 6.40m, 2.80m, 20.80m),
        };

        var top = TopPerformers.Find(team, hasAbvTarget: true);

        Assert.Equal(new[] { "ABV", "AUB", "P&L" }, top.Select(t => t.Measure));
        Assert.Equal(new[] { "Sam Parker" }, top[0].Names);
        Assert.Equal(new[] { "Dana Miller" }, top[1].Names);
        Assert.Equal(new[] { "Bailey Rowe" }, top[2].Names);
        Assert.Equal(35.64m, top[2].Value);
    }

    [Fact]
    public void Find_IgnoresPeopleUnderTheMinimumTransactions()
    {
        var team = new[]
        {
            Person("One Big Sale", 1, 48.00m, 6.00m, 42.64m),
            Person("Dana Miller", 28, 6.29m, 3.89m, 26.04m),
        };

        var top = TopPerformers.Find(team, hasAbvTarget: true);

        Assert.All(top, t => Assert.Equal(new[] { "Dana Miller" }, t.Names));
    }

    [Fact]
    public void Find_NamesEveryoneOnATie()
    {
        var team = new[]
        {
            Person("Dana Miller", 20, 6.00m, 3.00m, 12.80m),
            Person("Sam Parker", 20, 6.00m, 2.50m, 12.80m),
        };

        var top = TopPerformers.Find(team, hasAbvTarget: true);

        Assert.Equal(new[] { "Dana Miller", "Sam Parker" }, top[0].Names);
    }

    [Fact]
    public void Find_SkipsProfitLossWithoutAnAbvTarget_AndReturnsNothingWhenNobodyQualifies()
    {
        var team = new[] { Person("Dana Miller", 28, 6.29m, 3.89m, 0m) };
        Assert.Equal(new[] { "ABV", "AUB" }, TopPerformers.Find(team, hasAbvTarget: false).Select(t => t.Measure));
        Assert.Empty(TopPerformers.Find(new[] { Person("Sam Parker", 3, 9m, 4m, 5m) }, hasAbvTarget: true));
    }
}
