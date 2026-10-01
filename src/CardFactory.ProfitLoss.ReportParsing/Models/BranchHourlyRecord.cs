namespace CardFactory.ProfitLoss.ReportParsing.Models;

public sealed record BranchHourlyRecord(
    string TimeBand,
    int Transactions,
    int Units,
    decimal Sales,
    decimal Aub,
    decimal Abv,
    decimal PercentOfSales);
