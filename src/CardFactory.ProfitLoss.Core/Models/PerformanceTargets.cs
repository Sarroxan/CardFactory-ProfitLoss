namespace CardFactory.ProfitLoss.Core.Models;

/// <summary>
/// Performance targets used by the calculator. A target at or below zero is treated as not set.
/// </summary>
public sealed record PerformanceTargets(
    decimal SalesTarget = 0m,
    decimal AbvTarget = 0m,
    decimal AubTarget = 0m)
{
    public static PerformanceTargets None { get; } = new();
}
