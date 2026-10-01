using CardFactory.ProfitLoss.Core.Models;
using CardFactory.ProfitLoss.ReportParsing.Exceptions;
using CardFactory.ProfitLoss.ReportParsing.Internal;
using CardFactory.ProfitLoss.ReportParsing.Models;

namespace CardFactory.ProfitLoss.ReportParsing.Matching;

/// <summary>
/// Combines the Refunds report (master operator list) with Gift Card totals.
/// </summary>
public sealed class OperatorReportMerger
{
    public IReadOnlyList<OperatorInput> Merge(
        RefundReportParseResult refunds,
        GiftCardReportParseResult giftCards)
    {
        ArgumentNullException.ThrowIfNull(refunds);
        ArgumentNullException.ThrowIfNull(giftCards);

        ValidateReportPair(refunds.Metadata, giftCards.Metadata);

        var refundsById = BuildRefundIndexById(refunds.Operators);
        var refundsByName = BuildRefundIndexByName(refunds.Operators);
        var giftCardsByRefundOperatorId = new Dictionary<string, GiftCardOperatorRecord>(StringComparer.Ordinal);

        foreach (var giftCard in giftCards.Operators)
        {
            var refundOperator = MatchGiftCardOperator(giftCard, refundsById, refundsByName);

            if (!giftCardsByRefundOperatorId.TryAdd(refundOperator.OperatorId, giftCard))
            {
                throw new ReportMergeException(
                    $"More than one Gift Card result matched Refunds operator '{refundOperator.Name}' ({refundOperator.OperatorId}).");
            }
        }

        return refunds.Operators
            .Select(refundOperator =>
            {
                giftCardsByRefundOperatorId.TryGetValue(refundOperator.OperatorId, out var giftCard);

                return new OperatorInput(
                    refundOperator.Name,
                    refundOperator.Sales,
                    giftCard?.Value ?? 0m,
                    giftCard?.Quantity ?? 0,
                    refundOperator.Transactions,
                    refundOperator.Units);
            })
            .ToArray();
    }

    private static RefundOperatorRecord MatchGiftCardOperator(
        GiftCardOperatorRecord giftCard,
        IReadOnlyDictionary<string, RefundOperatorRecord> refundsById,
        IReadOnlyDictionary<string, IReadOnlyList<RefundOperatorRecord>> refundsByName)
    {
        if (!string.IsNullOrWhiteSpace(giftCard.OperatorId) &&
            refundsById.TryGetValue(giftCard.OperatorId, out var idMatch))
        {
            return idMatch;
        }

        var normalizedName = ReportText.NormalizeOperatorName(giftCard.Name);
        if (string.IsNullOrWhiteSpace(normalizedName) || !refundsByName.TryGetValue(normalizedName, out var nameMatches))
        {
            throw new ReportMergeException(
                $"Gift Card operator '{giftCard.Name}' ({giftCard.OperatorId}) was not found in the Refunds report.");
        }

        if (nameMatches.Count != 1)
        {
            throw new ReportMergeException(
                $"Gift Card operator '{giftCard.Name}' could not be matched safely because {nameMatches.Count} Refunds operators have the same normalised name.");
        }

        var nameMatch = nameMatches[0];
        if (!string.IsNullOrWhiteSpace(giftCard.OperatorId) &&
            !string.IsNullOrWhiteSpace(nameMatch.OperatorId) &&
            !string.Equals(giftCard.OperatorId, nameMatch.OperatorId, StringComparison.Ordinal))
        {
            throw new ReportMergeException(
                $"Gift Card operator '{giftCard.Name}' has ID '{giftCard.OperatorId}', but the matching Refunds name has ID '{nameMatch.OperatorId}'. The reports will not be merged by name when IDs conflict.");
        }

        return nameMatch;
    }

    private static IReadOnlyDictionary<string, RefundOperatorRecord> BuildRefundIndexById(
        IReadOnlyList<RefundOperatorRecord> operators)
    {
        var result = new Dictionary<string, RefundOperatorRecord>(StringComparer.Ordinal);
        foreach (var item in operators)
        {
            if (string.IsNullOrWhiteSpace(item.OperatorId))
            {
                continue;
            }

            if (!result.TryAdd(item.OperatorId, item))
            {
                throw new ReportMergeException($"Refunds report contains duplicate operator ID '{item.OperatorId}'.");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<RefundOperatorRecord>> BuildRefundIndexByName(
        IReadOnlyList<RefundOperatorRecord> operators)
    {
        return operators
            .GroupBy(item => ReportText.NormalizeOperatorName(item.Name), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RefundOperatorRecord>)group.ToArray(),
                StringComparer.Ordinal);
    }

    private static void ValidateReportPair(ParsedReportMetadata refunds, ParsedReportMetadata giftCards)
    {
        var refundsPeriodKnown = refunds.FromDate != DateOnly.MinValue && refunds.ToDate != DateOnly.MinValue;
        var giftCardsPeriodKnown = giftCards.FromDate != DateOnly.MinValue && giftCards.ToDate != DateOnly.MinValue;
        if (refundsPeriodKnown && giftCardsPeriodKnown &&
            (refunds.FromDate != giftCards.FromDate || refunds.ToDate != giftCards.ToDate))
        {
            throw new ReportMergeException(
                $"Report periods do not match. Refunds={refunds.FromDate:dd/MM/yyyy}-{refunds.ToDate:dd/MM/yyyy}; " +
                $"Gift Cards={giftCards.FromDate:dd/MM/yyyy}-{giftCards.ToDate:dd/MM/yyyy}.");
        }

        if (!string.IsNullOrWhiteSpace(refunds.Outlet) &&
            !string.IsNullOrWhiteSpace(giftCards.Outlet) &&
            !string.Equals(
                ReportText.NormalizeOutlet(refunds.Outlet),
                ReportText.NormalizeOutlet(giftCards.Outlet),
                StringComparison.Ordinal))
        {
            throw new ReportMergeException(
                $"Report outlets do not match. Refunds='{refunds.Outlet}'; Gift Cards='{giftCards.Outlet}'.");
        }
    }
}
