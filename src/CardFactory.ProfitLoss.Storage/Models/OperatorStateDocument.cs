namespace CardFactory.ProfitLoss.Storage.Models;

public sealed class OperatorStateDocument
{
    public string Name { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal GiftCardValue { get; set; }
    public int GiftCardQuantity { get; set; }
    public int Transactions { get; set; }
    public int Units { get; set; }
}
