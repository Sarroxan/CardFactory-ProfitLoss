namespace CardFactory.ProfitLoss.ReportParsing.Models;

/// <summary>
/// One line of "Discounts &amp; Price Overrides" (Detailed). Selling Price, Discount Price and
/// Discount Value are line totals - Flooid multiplies by Quantity before printing them (a
/// quantity of 3 at 2.49 shows a Selling Price of 7.47).
/// </summary>
public sealed record DiscountLineRecord(
    string ProductCode,
    string SellingCode,
    string Description,
    string DiscountType,
    string Reason,
    DateOnly Date,
    string OperatorId,
    decimal Quantity,
    decimal SellingPrice,
    decimal DiscountPrice,
    decimal DiscountValue,
    decimal DiscountPercent)
{
    /// <summary>
    /// True for a staff discount. Flooid's reason text is "25% Staff Discount" on the saved
    /// report; matching on the word rather than the percentage keeps another rate counting.
    /// </summary>
    public bool IsStaffDiscount => Reason.Contains("staff", StringComparison.OrdinalIgnoreCase);
}
