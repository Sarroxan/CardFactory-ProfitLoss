namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record BranchPerformanceTotal(
    int Transactions,
    int Units,
    decimal Sales,
    decimal Aub,
    decimal Abv,
    decimal PercentOfSales);
