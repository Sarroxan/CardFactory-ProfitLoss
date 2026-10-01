namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// Raw operator figures read from Refunds, Voids & No Sales - By Operator - Summary.
/// </summary>
public sealed record RefundOperatorRecord(
    string OperatorId,
    string Name,
    decimal Sales,
    int Transactions,
    int Units);
