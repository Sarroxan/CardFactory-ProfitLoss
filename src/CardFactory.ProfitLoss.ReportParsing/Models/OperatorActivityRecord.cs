namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// One operator's voids, refunds and no-sales from "Refunds, Voids &amp; No Sales - By Operator".
/// Read from the same report as the Team figures, so the Reports section needs no extra retrieval.
/// </summary>
public sealed record OperatorActivityRecord(
    string OperatorId,
    string Name,
    int TransactionVoidsQuantity,
    decimal TransactionVoidsValue,
    int LineVoidsQuantity,
    decimal LineVoidsValue,
    int ReceiptedRefundsQuantity,
    decimal ReceiptedRefundsValue,
    int KeyedRefundsQuantity,
    decimal KeyedRefundsValue,
    decimal TotalRefundsValue,
    int NoSales)
{
    public int VoidsQuantity => TransactionVoidsQuantity + LineVoidsQuantity;
    public decimal VoidsValue => TransactionVoidsValue + LineVoidsValue;
    public int RefundsQuantity => ReceiptedRefundsQuantity + KeyedRefundsQuantity;
}
