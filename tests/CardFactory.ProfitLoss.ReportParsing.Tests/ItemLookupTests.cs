using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;
using Xunit;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class ItemLookupTests
{
    // The cut-down real all-items report (page 19): Staff A and Staff B.
    private static IReadOnlyList<GiftCardItemRecord> Lines() =>
        new AllItemSalesReportParser().Parse(TestFixtureLoader.Read("ItemSalesAllItems_2026-11-16.html")).Items;

    [Fact]
    public void BySku_AddsPeopleTogether_AndSaysWhoSoldIt()
    {
        var best = ItemLookup.Find(Lines(), "2145811")[0];

        Assert.Equal("2145811", best.Code);
        Assert.Equal("XM26 FrostG Navy Snow MP Bag", best.Name);
        Assert.Equal(3, best.Quantity);
        Assert.Equal(2.48m, best.Value);
        Assert.Equal(new[] { "Staff A", "Staff B" }, best.Sellers.Select(s => s.OperatorName));
        Assert.Equal(new[] { 2, 1 }, best.Sellers.Select(s => s.Quantity));
        Assert.EndsWith("Wrap & Bags › Gift Bags", best.Department);
    }

    [Fact]
    public void BySku_WithoutTheLeadingZero()
    {
        var best = ItemLookup.Find(Lines(), "31085")[0];

        Assert.Equal("Dad From Both", best.Name);
        Assert.Equal(2, best.Quantity);
        Assert.Equal(2.98m, best.Value);
    }

    [Fact]
    public void ByWordsInTheName_InAnyOrder_MostSoldFirst()
    {
        var found = ItemLookup.Find(Lines(), "  heritage   XM26 ");

        Assert.Equal(new[] { "2145837", "2145842" }, found.Select(f => f.Code));
        Assert.Equal(3, found[0].Quantity);
    }

    [Fact]
    public void NothingTyped_OrNothingMatches_FindsNothing()
    {
        Assert.Empty(ItemLookup.Find(Lines(), "   "));
        Assert.Empty(ItemLookup.Find(Lines(), "no such item"));
    }

    [Fact]
    public void Department_DropsRepeatedLevels()
    {
        Assert.Equal("Stamps & Carrier Bags › Stamps", ItemLookup.Readable("Stamps & Carrier Bags/Stamps & Carrier Bags/Stamps & Carrier Bags/Stamps"));
    }
}
