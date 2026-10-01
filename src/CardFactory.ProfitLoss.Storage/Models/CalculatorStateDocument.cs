namespace CardFactory.ProfitLoss.Storage.Models;

/// <summary>
/// Calculator-only state. Flooid browser cookies, credentials and security tokens are deliberately excluded.
/// </summary>
public sealed class CalculatorStateDocument
{
    public int FormatVersion { get; set; } = 3;
    public string StoreLocation { get; set; } = string.Empty;
    public DateTime? SelectedDate { get; set; }
    /// <summary>First day of the Weekly range. SelectedDate is the last day.</summary>
    public DateTime? RangeStartDate { get; set; }
    public string HistoricalTargetMode { get; set; } = "Daily";
    public decimal SalesTarget { get; set; }
    public decimal AbvTarget { get; set; }
    public decimal AubTarget { get; set; }
    public List<OperatorStateDocument> Operators { get; set; } = new();
    public List<BranchHourlyStateDocument> BranchHourlyRows { get; set; } = new();
    public List<BranchGiftCardStateDocument> BranchGiftCards { get; set; } = new();
    public bool BranchReportDriven { get; set; }
    public decimal BranchReportedAbv { get; set; }
    public decimal BranchReportedAub { get; set; }
}
