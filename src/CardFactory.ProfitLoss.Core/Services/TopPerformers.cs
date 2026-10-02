using CardFactory.ProfitLoss.Core.Models;

namespace CardFactory.ProfitLoss.Core.Services;

/// <summary>The best ABV, AUB or P&amp;L in the team, and who achieved it (more than one on a tie).</summary>
public sealed record TopPerformer(string Measure, IReadOnlyList<string> Names, decimal Value);

/// <summary>
/// Picks the team's top performers for ABV, AUB and P&amp;L.
/// Only people with at least <see cref="MinimumTransactions"/> transactions qualify, so one
/// big sale cannot top the averages. Everyone level with the best value is named. P&amp;L is
/// only ranked when an ABV target is set, because without one it is not a profit or loss.
/// </summary>
public static class TopPerformers
{
    public const int MinimumTransactions = 10;

    public static IReadOnlyList<TopPerformer> Find(IEnumerable<OperatorPerformance> operators, bool hasAbvTarget)
    {
        var qualifying = operators
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && o.Transactions >= MinimumTransactions)
            .ToList();
        if (qualifying.Count == 0) return Array.Empty<TopPerformer>();

        var result = new List<TopPerformer>
        {
            Best("ABV", qualifying, o => Math.Round(o.Abv, 2)),
            Best("AUB", qualifying, o => Math.Round(o.Aub, 2)),
        };
        if (hasAbvTarget) result.Add(Best("P&L", qualifying, o => Math.Round(o.ProfitLoss, 2)));
        return result;
    }

    private static TopPerformer Best(string measure, IReadOnlyList<OperatorPerformance> people, Func<OperatorPerformance, decimal> value)
    {
        var best = people.Max(value);
        var names = people.Where(o => value(o) == best).Select(o => o.Name.Trim()).ToList();
        return new TopPerformer(measure, names, best);
    }
}
