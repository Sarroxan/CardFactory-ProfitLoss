namespace CardFactory.ProfitLoss.Storage.Models;

public sealed class BranchGiftCardStateDocument
{
    public string Item { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Sales { get; set; }
}
