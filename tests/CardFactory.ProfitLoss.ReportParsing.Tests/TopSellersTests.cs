using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;
using Xunit;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class TopSellersTests
{
    // The cut-down real all-items report (page 19): Staff A and Staff B, cards, gift bags,
    // carrier bags and a stamp book.
    private static IReadOnlyList<TopSeller> FromFixture() =>
        TopSellers.Build(new AllItemSalesReportParser().Parse(TestFixtureLoader.Read("ItemSalesAllItems_2026-11-16.html")).Items);

    [Fact]
    public void People_AreAddedTogether_AndCarrierBagsAreLeftOut()
    {
        var all = FromFixture();

        Assert.DoesNotContain(all, t => t.Code == "EFOB012");
        Assert.Equal(32, all.Count);

        // Staff A 2 + Staff B 1.
        var navy = Assert.Single(all, t => t.Code == "2145811");
        Assert.Equal(3, navy.Quantity);
        Assert.Equal(2.48m, navy.Value);
        var dad = Assert.Single(all, t => t.Code == "0031085");
        Assert.Equal(2, dad.Quantity);
        Assert.Equal(2.98m, dad.Value);
    }

    [Fact]
    public void ByNumberSold_ThenByValue()
    {
        var top = TopSellers.Top(FromFixture(), TopSellerGroup.All, byValue: false);

        Assert.Equal(new[] { "2145706", "2145837", "2145811", "2145708" }, top.Take(4).Select(t => t.Code));
        Assert.Equal(7, top[0].Quantity);
        Assert.Equal(25, top.Count);
    }

    [Fact]
    public void ByValue()
    {
        var top = TopSellers.Top(FromFixture(), TopSellerGroup.All, byValue: true);

        Assert.Equal(new[] { "6001037", "2145706", "2145837" }, top.Take(3).Select(t => t.Code));
    }

    [Fact]
    public void Groups_FromTheDepartmentPath()
    {
        var all = FromFixture();

        Assert.Equal("0031085", TopSellers.Top(all, TopSellerGroup.Cards, false)[0].Code);
        Assert.All(TopSellers.Top(all, TopSellerGroup.Cards, false), t => Assert.Equal(TopSellerGroup.Cards, t.Group));
        Assert.Equal("2145706", TopSellers.Top(all, TopSellerGroup.WrapAndBags, false)[0].Code);
        Assert.Empty(TopSellers.Top(all, TopSellerGroup.PartyAndGifting, false));
        Assert.Equal(new[] { "6001037" }, TopSellers.Top(all, TopSellerGroup.StampsAndOther, false).Select(t => t.Code));

        Assert.Equal(TopSellerGroup.PartyAndGifting, TopSellers.GroupOf("Seasonal Party & Gifting/Christmas Party & Gifting/Gifts/Soft Toys"));
        Assert.Equal(TopSellerGroup.Cards, TopSellers.GroupOf("Seasonal Card/Christmas Card/Core/Open"));
        Assert.Equal(TopSellerGroup.All, TopSellers.GroupOf(""));
    }

    [Fact]
    public void GiftCards_AreLeftOut()
    {
        var gift = new GiftCardItemRecord("1", "A", "9000001", "Gift Card", 2, 40m) { Department = AllItemSalesReportParser.GiftCardsDepartment };
        var card = new GiftCardItemRecord("1", "A", "2117285", "Mum", 1, 1.99m) { Department = "Everyday Card/Everyday Card/Core/Female" };

        var all = TopSellers.Build(new[] { gift, card });

        Assert.Equal(new[] { "2117285" }, all.Select(t => t.Code));
    }
}
