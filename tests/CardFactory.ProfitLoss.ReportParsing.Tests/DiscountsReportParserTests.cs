using Xunit;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class DiscountsReportParserTests
{
    // Saved from the live system on 10/10/2026 (Transaction Discount / 25% Staff Discount),
    // with the outlet and operator replaced the same way as the other fixtures.
    private const string Fixture = "DiscountsAndPriceOverrides_2026-10-10.html";

    [Fact]
    public void Parse_GenuineReport_ReadsEveryLine()
    {
        var result = new DiscountsReportParser().Parse(TestFixtureLoader.Read(Fixture));

        Assert.Equal(4, result.Lines.Count);
        Assert.Equal(new DateOnly(2026, 10, 10), result.Metadata.FromDate);
        Assert.Equal(new DateOnly(2026, 10, 10), result.Metadata.ToDate);
        Assert.Equal("0001 - Sample Store", result.Metadata.Outlet);
        Assert.Equal("Transaction Discount", result.DiscountTypeSelection);
        Assert.Equal("25% Staff Discount", result.ReasonSelection);

        var wheels = Assert.Single(result.Lines, line => line.ProductCode == "2159953");
        Assert.Equal("Hot Wheels Mini Figs", wheels.Description);
        Assert.Equal("027084120134", wheels.SellingCode);
        Assert.Equal("Transaction Discount", wheels.DiscountType);
        Assert.Equal("25% Staff Discount", wheels.Reason);
        Assert.Equal(new DateOnly(2026, 10, 10), wheels.Date);
        Assert.Equal("20000002", wheels.OperatorId);
        Assert.Equal(3m, wheels.Quantity);
        Assert.Equal(7.47m, wheels.SellingPrice);
        Assert.Equal(5.61m, wheels.DiscountPrice);
        Assert.Equal(1.86m, wheels.DiscountValue);
        Assert.Equal(25m, wheels.DiscountPercent);
        Assert.True(wheels.IsStaffDiscount);

        // The report's own REPORT TOTAL row: 6.00 items, 2.61 discount.
        Assert.Equal(6m, result.Lines.Sum(line => line.Quantity));
        Assert.Equal(2.61m, result.StaffDiscountLines.Sum(line => line.DiscountValue));
    }

    [Fact]
    public void Parse_OtherReasons_AreNotStaffDiscount()
    {
        var html = TestFixtureLoader.Read(Fixture);
        var first = html.IndexOf("<td>25% Staff Discount</td>", StringComparison.Ordinal);
        html = html[..first] + "<td>Damaged</td>" + html[(first + "<td>25% Staff Discount</td>".Length)..];

        var result = new DiscountsReportParser().Parse(html);

        Assert.Equal(4, result.Lines.Count);
        Assert.Equal(3, result.StaffDiscountLines.Count);
        Assert.Equal(2.36m, result.StaffDiscountLines.Sum(line => line.DiscountValue));
    }

    [Fact]
    public void Parse_TotalsThatDoNotAddUp_AreRejected()
    {
        var html = TestFixtureLoader.Read(Fixture).Replace("<td>2.61</td>", "<td>2.62</td>", StringComparison.Ordinal);

        var error = Assert.Throws<ReportParseException>(() => new DiscountsReportParser().Parse(html));
        Assert.Contains("REPORT TOTAL", error.Message);
    }

    [Fact]
    public void Parse_MissingColumn_IsRejected()
    {
        var html = TestFixtureLoader.Read(Fixture).Replace(">Discount Value<", ">Discount Amount<", StringComparison.Ordinal);

        var error = Assert.Throws<ReportParseException>(() => new DiscountsReportParser().Parse(html));
        Assert.Contains("Discount Value", error.Message);
    }

    [Fact]
    public void Parse_AnotherReport_IsRejected()
    {
        var html = TestFixtureLoader.Read("RefundsVoidsByOperator_2026-08-20.html");

        Assert.Throws<ReportParseException>(() => new DiscountsReportParser().Parse(html));
    }
}
