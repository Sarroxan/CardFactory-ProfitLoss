using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;
using Xunit;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class StampsSoldTests
{
    // The owner's real save (page 15, 19 Oct 2026), names and store replaced.
    private const string Fixture = "ItemSalesStamps_2026-10-19.html";

    [Fact]
    public void RealStampsReport_ReadsTheSecondClassBook()
    {
        var result = new GiftCardReportParser("Stamps").Parse(TestFixtureLoader.Read(Fixture));

        Assert.Equal("Stamps", result.ProductGroups);
        var item = Assert.Single(result.Items);
        Assert.Equal("6001037", item.ProductCode);
        Assert.Equal("8x2ND Class Stamp Book", item.Description);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(7.28m, item.Value);
    }

    [Fact]
    public void StampsReport_IsRefused_AsGiftCards()
    {
        Assert.Throws<ReportParseException>(() => new GiftCardReportParser().Parse(TestFixtureLoader.Read(Fixture)));
    }

    [Theory]
    [InlineData("Christmas 2nd Class Stamps", true)]
    [InlineData("XMAS 1ST CLASS STAMP BOOK", true)]
    [InlineData("8x2ND Class Stamp Book", false)]
    public void ChristmasStamps_AreRecognised(string description, bool christmas) =>
        Assert.Equal(christmas, StampsSold.IsChristmas(description));
}
