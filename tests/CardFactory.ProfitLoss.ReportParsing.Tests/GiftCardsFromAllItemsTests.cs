using CardFactory.ProfitLoss.ReportParsing.Parsers;
using Xunit;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

/// <summary>
/// Speed (16 Nov 2026): Gift Cards and Stamps read from the all-items report instead of their own
/// pulls. The department names come from the real Gift Cards and Stamps reports; the figures
/// must be exactly what those reports give.
/// </summary>
public sealed class GiftCardsFromAllItemsTests
{
    private const string Constructed = "ItemSalesAllItemsGiftCards_constructed.html";

    [Fact]
    public void RealGiftCardsReport_PrintsTheGiftCardsDepartment()
    {
        var report = new GiftCardReportParser().Parse(TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html"));
        var line = Assert.Single(report.Items);
        Assert.Equal(AllItemSalesReportParser.GiftCardsDepartment, line.Department);

        // Read back through the new path, the real report gives its own figures.
        var again = AllItemSalesReportParser.GiftCards(report);
        Assert.Equal(report.Operators, again.Operators);
        Assert.Equal(report.Items, again.Items);
    }

    [Fact]
    public void RealStampsReport_PrintsTheStampsDepartment()
    {
        var report = new GiftCardReportParser("Stamps").Parse(TestFixtureLoader.Read("ItemSalesStamps_2026-10-19.html"));
        Assert.All(report.Items, i => Assert.Equal(AllItemSalesReportParser.StampsDepartment, i.Department));
        Assert.Equal(report.Items, AllItemSalesReportParser.Stamps(report));
    }

    [Fact]
    public void RealAllItemsReport_StampsAreItsStampLinesOnly_AndNoGiftCards()
    {
        var all = new AllItemSalesReportParser().Parse(TestFixtureLoader.Read("ItemSalesAllItems_2026-11-16.html"));

        var stamp = Assert.Single(AllItemSalesReportParser.Stamps(all));
        Assert.Equal("6001037", stamp.ProductCode);
        Assert.Equal(7.28m, stamp.Value);
        // The carrier bags sit next to the stamps in the same group and must not count.
        Assert.DoesNotContain(AllItemSalesReportParser.Stamps(all), i => i.ProductCode == "EFOB012");

        var gift = AllItemSalesReportParser.GiftCards(all);
        Assert.Empty(gift.Operators);
        Assert.Empty(gift.Items);
        Assert.Equal(all.Metadata, gift.Metadata);
    }

    [Fact]
    public void AllItemsReport_WithGiftCards_GivesTheGiftCardsPerPerson()
    {
        var all = new AllItemSalesReportParser().Parse(TestFixtureLoader.Read(Constructed));
        var gift = AllItemSalesReportParser.GiftCards(all);

        Assert.Equal("Gift Cards", gift.ProductGroups);
        Assert.Equal(2, gift.Operators.Count);
        Assert.Equal(("90000001", "Staff A", 1, 20.00m), (gift.Operators[0].OperatorId, gift.Operators[0].Name, gift.Operators[0].Quantity, gift.Operators[0].Value));
        Assert.Equal(("90000002", "Staff B", 3, 70.00m), (gift.Operators[1].OperatorId, gift.Operators[1].Name, gift.Operators[1].Quantity, gift.Operators[1].Value));
        Assert.Equal(3, gift.Items.Count);
        Assert.All(gift.Items, i => Assert.StartsWith("GC ", i.Description));

        // The rest of the report reads as before, totals included.
        Assert.Equal(66, all.Items.Sum(i => i.Quantity));
        Assert.Equal(150.89m, all.Items.Sum(i => i.Value));
        Assert.Single(AllItemSalesReportParser.Stamps(all));
    }

    [Fact]
    public void GiftCardsFromAllItems_KeepTheStoreAndDates_TheMergeChecks()
    {
        // The merge checks the two reports share a store and dates; the all-items report's own
        // heading carries them, as the Gift Cards report's did.
        var all = new AllItemSalesReportParser().Parse(TestFixtureLoader.Read(Constructed));
        var gift = AllItemSalesReportParser.GiftCards(all);
        Assert.Equal(new DateOnly(2026, 11, 16), gift.Metadata.FromDate);
        Assert.Equal(new DateOnly(2026, 11, 16), gift.Metadata.ToDate);
        Assert.False(string.IsNullOrWhiteSpace(gift.Metadata.Outlet));
    }
}
