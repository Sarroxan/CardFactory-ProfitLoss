namespace CardFactory.ProfitLoss.Storage.Models;

public sealed class BranchHourlyStateDocument
{
    public string TimeBand { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public int Transactions { get; set; }
    public int Units { get; set; }
    public decimal Abv { get; set; }
    public decimal Aub { get; set; }
    public decimal PercentOfSales { get; set; }
    public bool IsReportDriven { get; set; }
}
