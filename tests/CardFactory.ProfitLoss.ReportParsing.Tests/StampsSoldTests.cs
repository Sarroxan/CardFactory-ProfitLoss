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

        var sold = StampsSold.From(result.Items);
        Assert.Equal(new StampsSold(0, 1, 0, 0), sold);
        Assert.Equal(1, sold.Total);
    }

    [Fact]
    public void StampsReport_IsRefused_AsGiftCards()
    {
        Assert.Throws<ReportParseException>(() => new GiftCardReportParser().Parse(TestFixtureLoader.Read(Fixture)));
    }

    [Theory]
    [InlineData("8x2ND Class Stamp Book", "2nd")]
    [InlineData("12x1ST Class Stamp Book", "1st")]
    [InlineData("First Class Stamp", "1st")]
    [InlineData("Second class stamp", "2nd")]
    [InlineData("Large 1st Class Stamp", "Large")]
    [InlineData("4x Large Letter 2nd Class", "Large")]
    [InlineData("Carrier Bag", null)]
    public void Lines_AreMatchedByTheirName(string description, string? expected) =>
        Assert.Equal(expected, StampsSold.Classify(description));

    [Fact]
    public void Quantities_AddUpPerKind_AndNothingIsLost()
    {
        GiftCardItemRecord Line(string d, int q) => new("1", "A", "0", d, q, 1m);
        var sold = StampsSold.From(new[]
        {
            Line("8x2ND Class Stamp Book", 2), Line("12x1ST Class Stamp Book", 1),
            Line("Large 1st Class Stamp", 3), Line("Mystery item", 1),
        });
        Assert.Equal(new StampsSold(1, 2, 3, 1), sold);
        Assert.Equal(7, sold.Total);
    }
}
