using Xunit;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Matching;
using CardFactory.ProfitLoss.ReportParsing.Models;
using CardFactory.ProfitLoss.ReportParsing.Parsers;

namespace CardFactory.ProfitLoss.ReportParsing.Tests;

public sealed class OperatorReportMergerTests
{
    [Fact]
    public void Merge_GenuineReports_KeepsAllRefundOperatorsAndAppliesGiftCardsToBailey()
    {
        var refunds = new RefundsVoidsReportParser().Parse(
            TestFixtureLoader.Read("RefundsVoidsByOperator_2026-08-20.html"));
        var giftCards = new GiftCardReportParser().Parse(
            TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html"));

        var result = new OperatorReportMerger().Merge(refunds, giftCards);

        Assert.Equal(3, result.Count);

        var dana = Assert.Single(result, item => item.Name == "Dana Miller");
        Assert.Equal(79.46m, dana.Sales);
        Assert.Equal(0m, dana.GiftCardValue);
        Assert.Equal(0, dana.GiftCardQuantity);
        Assert.Equal(13, dana.Transactions);
        Assert.Equal(42, dana.Units);

        var sam = Assert.Single(result, item => item.Name == "Sam Parker");
        Assert.Equal(0m, sam.GiftCardValue);
        Assert.Equal(0, sam.GiftCardQuantity);

        var bailey = Assert.Single(result, item => item.Name == "Bailey Rowe");
        Assert.Equal(1157.57m, bailey.Sales);
        Assert.Equal(20.00m, bailey.GiftCardValue);
        Assert.Equal(1, bailey.GiftCardQuantity);
        Assert.Equal(284, bailey.Transactions);
        Assert.Equal(731, bailey.Units);
    }

    [Fact]
    public void Merge_SameReportsTwice_ReturnsIdenticalData()
    {
        var refunds = new RefundsVoidsReportParser().Parse(
            TestFixtureLoader.Read("RefundsVoidsByOperator_2026-08-20.html"));
        var giftCards = new GiftCardReportParser().Parse(
            TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html"));
        var merger = new OperatorReportMerger();

        var first = merger.Merge(refunds, giftCards);
        var second = merger.Merge(refunds, giftCards);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Merge_DifferentPeriods_IsRejected()
    {
        var refunds = new RefundsVoidsReportParser().Parse(
            TestFixtureLoader.Read("RefundsVoidsByOperator_2026-08-20.html"));
        var giftCards = new GiftCardReportParser().Parse(
            TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html"));
        giftCards = giftCards with
        {
            Metadata = giftCards.Metadata with { ToDate = new DateOnly(2026, 8, 21) }
        };

        var error = Assert.Throws<ReportMergeException>(() => new OperatorReportMerger().Merge(refunds, giftCards));

        Assert.Contains("periods do not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merge_ConflictingOperatorIds_DoesNotSilentlyMatchByName()
    {
        var metadata = new ParsedReportMetadata(
            "Test",
            "0001 - Sample Store",
            new DateOnly(2026, 8, 20),
            new DateOnly(2026, 8, 20),
            new DateTime(2026, 8, 20, 20, 0, 0),
            "All");

        var refunds = new RefundReportParseResult(
            metadata,
            new[] { new RefundOperatorRecord("111", "Example Operator", 10m, 1, 1) });
        var giftCards = new GiftCardReportParseResult(
            metadata,
            "Gift Cards",
            "Show Item Detail",
            new[] { new GiftCardOperatorRecord("222", "Example Operator", 5m, 1) });

        var error = Assert.Throws<ReportMergeException>(() => new OperatorReportMerger().Merge(refunds, giftCards));

        Assert.Contains("IDs conflict", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merge_GiftCardOperatorNotInRefunds_IsRejected()
    {
        var refunds = new RefundsVoidsReportParser().Parse(
            TestFixtureLoader.Read("RefundsVoidsByOperator_2026-08-20.html"));
        var giftCards = new GiftCardReportParser().Parse(
            TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html"));
        giftCards = giftCards with
        {
            Operators = new[] { new GiftCardOperatorRecord("999999", "Unknown Operator", 20m, 1) }
        };

        var error = Assert.Throws<ReportMergeException>(() => new OperatorReportMerger().Merge(refunds, giftCards));

        Assert.Contains("was not found in the Refunds report", error.Message);
    }

    [Fact]
    public void Merge_UnknownGeneratedMetadata_DoesNotBlockOtherwiseSafeMerge()
    {
        var refunds = new RefundsVoidsReportParser().Parse(
            TestFixtureLoader.Read("RefundsVoidsByOperator_2026-08-20.html"));
        var giftCards = new GiftCardReportParser().Parse(
            TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html"));

        refunds = refunds with { Metadata = refunds.Metadata with { Outlet = string.Empty, FromDate = DateOnly.MinValue, ToDate = DateOnly.MinValue, CreatedAt = DateTime.MinValue } };
        giftCards = giftCards with { Metadata = giftCards.Metadata with { Outlet = string.Empty, FromDate = DateOnly.MinValue, ToDate = DateOnly.MinValue, CreatedAt = DateTime.MinValue } };

        var result = new OperatorReportMerger().Merge(refunds, giftCards);

        Assert.Equal(3, result.Count);
        Assert.Equal(20.00m, Assert.Single(result, item => item.Name == "Bailey Rowe").GiftCardValue);
    }

    [Fact]
    public void Merge_KnownDifferentOutlets_IsStillRejected()
    {
        var refunds = new RefundsVoidsReportParser().Parse(
            TestFixtureLoader.Read("RefundsVoidsByOperator_2026-08-20.html"));
        var giftCards = new GiftCardReportParser().Parse(
            TestFixtureLoader.Read("ItemSalesGiftCards_2026-08-20.html"));
        giftCards = giftCards with { Metadata = giftCards.Metadata with { Outlet = "9999 - Different Store" } };

        var error = Assert.Throws<ReportMergeException>(() => new OperatorReportMerger().Merge(refunds, giftCards));

        Assert.Contains("outlets do not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

}
