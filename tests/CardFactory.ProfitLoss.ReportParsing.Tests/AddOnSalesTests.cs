using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;
using Xunit;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class AddOnSalesTests
{
    // The owner's real save (page 19, 16 Nov 2026 12:24): Item Sales By Operator with every item,
    // cut down to five departments, names, numbers and store replaced, totals worked out again.
    private const string Fixture = "ItemSalesAllItems_2026-11-16.html";

    [Fact]
    public void RealAllItemsReport_ReadsEveryItemLine()
    {
        var result = new AllItemSalesReportParser().Parse(TestFixtureLoader.Read(Fixture));

        Assert.Equal("All", result.ProductGroups);
        Assert.Equal(2, result.Operators.Count);
        Assert.Equal(57, result.Operators[0].Quantity);
        Assert.Equal(55.68m, result.Operators[0].Value);
        Assert.Equal(5, result.Operators[1].Quantity);

        // Item lines under department headings are read the same as the Gift Cards lines.
        Assert.Equal(62, result.Items.Sum(i => i.Quantity));
        Assert.Equal(60.89m, result.Items.Sum(i => i.Value));

        var carrier = result.Items.Where(i => i.ProductCode == "EFOB012").ToList();
        Assert.Equal(2, carrier.Count);
        Assert.Equal("Carrier Bag", carrier[0].Description);
        Assert.Equal(12, carrier[0].Quantity);
        Assert.Equal(3.00m, carrier[0].Value);
        Assert.Equal("90000001", carrier[0].OperatorId);
        Assert.Equal("Staff B", carrier[1].OperatorName);

        var redBags = Assert.Single(result.Items, i => i.ProductCode == "2145706");
        Assert.Equal("VALUE XM26 Red LP", redBags.Description);
        Assert.Equal(7, redBags.Quantity);
        Assert.Equal(5.34m, redBags.Value);

        var stamp = Assert.Single(result.Items, i => i.ProductCode == "6001037");
        Assert.Equal("8x2ND Class Stamp Book", stamp.Description);
    }

    [Fact]
    public void GiftCardsReport_IsRefused_AsAllItems() =>
        Assert.Throws<ReportParseException>(() =>
            new AllItemSalesReportParser().Parse(TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html")));

    [Fact]
    public void AllItemsReport_NarrowedToOneProduct_IsRefused()
    {
        var html = TestFixtureLoader.Read(Fixture).Replace(">All</td></tr><tr><td class=\"selectionHeaderTitle\" width=\"40%\">Product Groups",
            ">2145706</td></tr><tr><td class=\"selectionHeaderTitle\" width=\"40%\">Product Groups", StringComparison.Ordinal);
        Assert.Contains(">2145706</td>", html);
        Assert.Throws<ReportParseException>(() => new AllItemSalesReportParser().Parse(html));
    }

    [Fact]
    public void TrackedSkus_AreAddedUpPerPersonAndInTotal()
    {
        var report = new AllItemSalesReportParser().Parse(TestFixtureLoader.Read(Fixture));
        var tracked = new[]
        {
            new AddOnItem("EFOB012", "Carrier Bag"),
            new AddOnItem("2145811", "Navy Snow Bag"),
            new AddOnItem("9999999", "Not sold today"),
        };

        var summary = AddOnSalesSummary.Build(tracked, report.Items, stampItems: null);

        Assert.Equal(new[] { 13, 3, 0 }, summary.Columns.Select(c => c.Quantity));
        Assert.Equal(3.25m, summary.Columns[0].Value);
        Assert.Equal(2.48m, summary.Columns[1].Value);
        Assert.Equal("Staff A", summary.Columns[0].TopSeller);
        Assert.Equal(12, summary.Columns[0].TopQuantity);
        Assert.Null(summary.Columns[2].TopSeller);

        Assert.Equal(2, summary.People.Count);
        Assert.Equal("Staff A", summary.People[0].Name);
        Assert.Equal(new[] { 12, 2, 0 }, summary.People[0].Quantities);
        Assert.Equal(14, summary.People[0].Total);
        Assert.Equal(new[] { 1, 1, 0 }, summary.People[1].Quantities);
        Assert.Equal(16, summary.TotalQuantity);
        Assert.Equal(5.73m, summary.TotalValue);
    }

    [Fact]
    public void Stamps_ComeFromTheStampsReport_ChristmasStampsIncluded()
    {
        GiftCardItemRecord Line(string id, string name, string code, string d, int q, decimal v) => new(id, name, code, d, q, v);
        var stamps = new[]
        {
            Line("1", "Staff A", "6001037", "8x2ND Class Stamp Book", 2, 14.56m),
            Line("2", "Staff B", "6001038", "XMAS 1ST CLASS STAMP BOOK", 3, 30m),
            Line("2", "Staff B", "6001039", "12x1ST Class Stamp Book", 1, 13.20m),
        };

        var summary = AddOnSalesSummary.Build(new[] { AddOnItem.Stamps }, Array.Empty<GiftCardItemRecord>(), stamps);

        // Owner, 26 Nov 2026: Christmas stamps count too.
        Assert.Equal(6, summary.Columns[0].Quantity);
        Assert.Equal(57.76m, summary.Columns[0].Value);
        Assert.Equal(new[] { "Staff A", "Staff B" }, summary.People.Select(p => p.Name));
    }

    [Fact]
    public void Top_KeepsTheBestSellers_InTheOrderTheyWereAdded()
    {
        GiftCardItemRecord Line(string id, string code, int q) => new(id, "Person " + id, code, code, q, q);
        var items = new[]
        {
            Line("1", "A", 1), Line("1", "B", 5), Line("2", "C", 3), Line("2", "D", 3), Line("3", "E", 9),
        };
        var tracked = "ABCDE".Select(c => new AddOnItem(c.ToString(), c.ToString())).ToList();

        var top = AddOnSalesSummary.Build(tracked, items, null).Top(4);

        // A sold least; C and D tie, so both stay; order as added.
        Assert.Equal(new[] { "B", "C", "D", "E" }, top.Columns.Select(c => c.Item.Key));
        Assert.Equal(new[] { "Person 3", "Person 2", "Person 1" }, top.People.Select(p => p.Name));
        Assert.Equal(new[] { 5, 0, 0, 0 }, top.People[2].Quantities);
    }

    [Theory]
    [InlineData("0031085", "31085", true)]
    [InlineData("efob012", " EFOB012 ", true)]
    [InlineData("2145811", "2145812", false)]
    [InlineData("EFOB012", "", false)]
    public void Skus_AreMatchedAsTyped(string reported, string typed, bool same) =>
        Assert.Equal(same, AddOnSalesSummary.SameSku(reported, typed));

    [Fact]
    public void ReusableBags_AllAddedTogether_AsOneColumn()
    {
        var items = new[]
        {
            new GiftCardItemRecord("1", "Staff A", "7000001", "Reusable Bag Large Floral", 3, 5.97m) { Department = TopSellers.CarrierBagsDepartment },
            new GiftCardItemRecord("2", "Staff B", "7000002", "REUSABLE BAG Christmas", 1, 1.99m),
            new GiftCardItemRecord("1", "Staff A", "EFOB012", "Carrier Bag", 12, 3.00m) { Department = TopSellers.CarrierBagsDepartment },
            new GiftCardItemRecord("2", "Staff B", "7000003", "Reusable Straw", 2, 2.00m),
        };

        var summary = AddOnSalesSummary.Build(new[] { AddOnItem.ReusableBags }, items, stampItems: null);

        var column = Assert.Single(summary.Columns);
        Assert.Equal(4, column.Quantity);
        Assert.Equal(7.96m, column.Value);
        Assert.Equal(2, summary.People.Count);
        Assert.True(AddOnItem.ReusableBags.IsGroup);
        Assert.False(new AddOnItem("7000001", "x").IsGroup);
    }

    [Fact]
    public void TrackAllNamed_MatchesNameOrDepartment_AsOneColumn()
    {
        const string giftBags = "Everyday Party & Gifting/Everyday Party & Gifting/Wrap & Bags/Gift Bags";
        var items = new[]
        {
            new GiftCardItemRecord("1", "Staff A", "2128720", "Pink BT Bag V", 1, 0.69m) { Department = giftBags },
            new GiftCardItemRecord("2", "Staff B", "2128770", "Birthday Floral LP Bag", 2, 2.98m) { Department = giftBags },
            new GiftCardItemRecord("2", "Staff B", "9000001", "Gift Card", 1, 20.00m) { Department = AllItemSalesParserGiftCards },
            new GiftCardItemRecord("1", "Staff A", "2128843", "Lilac Tissue", 1, 1.39m) { Department = "Everyday Party & Gifting/Everyday Party & Gifting/Wrap & Bags/Wrap Accessories" },
        };

        var named = AddOnItem.Named("  Gift   BAG ");
        Assert.Equal("NAMED:gift bag", named.Key);
        Assert.Equal("Gift BAG", named.Name);
        Assert.True(named.IsGroup);
        Assert.Equal("gift bag", named.Words);

        var column = Assert.Single(AddOnSalesSummary.Build(new[] { named }, items, stampItems: null).Columns);
        Assert.Equal(3, column.Quantity);
        Assert.Equal(3.67m, column.Value);

        var tissue = Assert.Single(AddOnSalesSummary.Build(new[] { AddOnItem.Named("tissue") }, items, null).Columns);
        Assert.Equal(1, tissue.Quantity);
    }

    private const string AllItemSalesParserGiftCards = "Stamps & Carrier Bags/Stamps & Carrier Bags/Stamps & Carrier Bags/Gift Cards";
}
