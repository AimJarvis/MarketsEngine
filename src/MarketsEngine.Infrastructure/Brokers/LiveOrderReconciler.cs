using KiteConnect;
using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Applies one Kite order update (status + any new fill) to a LiveOrder/LiveFill/LivePosition.
/// Shared by two independent paths that can both observe the same real-world event:
/// LiveOrderReconciliationService polling kite.GetOrders() as a guaranteed fallback, and
/// KiteTickerService's push-based Ticker.OnOrderUpdate handler for low latency when the ticker is
/// running. Idempotent against both the status AND the fill quantity independently - applying the
/// same brokerOrder twice is a no-op, so having both paths active at once is safe, not a race (the
/// DB's unique index on LiveFill backstops the narrow window where both genuinely race to record
/// the exact same new increment at once).
///
/// Fill detection is deliberately independent of the status transition: Kite has no distinct
/// "partially filled" status string - a partial fill just shows status Open with
/// FilledQuantity &lt; Quantity - so an order that fills in pieces and then gets cancelled (or
/// never reaches Complete) would otherwise have the quantity that DID fill at the broker silently
/// never recorded. Every observed increase in Kite's cumulative FilledQuantity is recorded as its
/// own LiveFill the moment it's seen, regardless of what the order's status does.
/// </summary>
public static class LiveOrderReconciler
{
    /// <summary>order must already have .Fills and .Instrument loaded by the caller.</summary>
    public static async Task ApplyUpdateAsync(
        MarketsEngineDbContext dbContext, LiveOrder order, Order brokerOrder, DateTime now, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var alreadyRecordedUnits = order.Fills.Sum(f => f.FilledQuantity);
        var brokerFilledUnits = (int)brokerOrder.FilledQuantity;
        var deltaUnits = brokerFilledUnits - alreadyRecordedUnits;

        if (deltaUnits > 0)
        {
            await RecordFillAsync(dbContext, order, brokerOrder, deltaUnits, brokerFilledUnits, now, logger, cancellationToken);
        }

        var newStatus = MapStatus(brokerOrder.Status);
        if (newStatus == order.Status)
        {
            return;
        }

        order.Status = newStatus;

        if (newStatus is LiveOrderStatus.Cancelled)
        {
            order.RejectionReason = brokerOrder.StatusMessage;
        }

        logger.LogInformation("Live order {OrderId} ({BrokerOrderId}) status updated to {Status}", order.Id, order.BrokerOrderId, newStatus);
    }

    private static async Task RecordFillAsync(
        MarketsEngineDbContext dbContext, LiveOrder order, Order brokerOrder, int deltaUnits, int cumulativeFilledUnits,
        DateTime now, ILogger logger, CancellationToken cancellationToken)
    {
        var lotSize = order.Instrument?.LotSize ?? 1;

        // Kite's AveragePrice is the CUMULATIVE average fill price across everything filled so
        // far for this order, not the price of just this new increment - back out the
        // incremental price from the cumulative notional value so each LiveFill records what this
        // specific piece actually filled at, not a blended average that double-counts earlier
        // fills.
        var previousNotional = order.Fills.Sum(f => f.FilledQuantity * f.FillPrice);
        var newTotalNotional = brokerOrder.AveragePrice * cumulativeFilledUnits;
        var deltaFillPrice = (newTotalNotional - previousNotional) / deltaUnits;

        var fill = new LiveFill
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FilledQuantity = deltaUnits,
            FillPrice = deltaFillPrice,
            FilledAtUtc = now,
            CumulativeFilledQuantityAfter = cumulativeFilledUnits
        };

        dbContext.LiveFills.Add(fill);
        order.Fills.Add(fill);

        if (deltaUnits % lotSize != 0)
        {
            // Kite fills F&O in whole lots and equities have lot size 1, so this should never
            // happen - but if it ever does, surface it loudly rather than silently mis-sizing the
            // position update below (which still proceeds, using the floored lot count).
            logger.LogWarning(
                "Live order {OrderId}: fill increment of {DeltaUnits} units is not a multiple of lot size {LotSize} - unexpected, investigate.",
                order.Id, deltaUnits, lotSize);
        }

        var deltaLots = deltaUnits / lotSize;
        if (deltaLots == 0)
        {
            return;
        }

        var position = await dbContext.LivePositions
            .FirstOrDefaultAsync(p => p.InstrumentId == order.InstrumentId, cancellationToken);

        var currentNetQuantity = position?.NetQuantity ?? 0;
        var currentAveragePrice = position?.AverageEntryPrice ?? 0m;

        var update = PositionUpdater.ApplyFill(currentNetQuantity, currentAveragePrice, order.Side, deltaLots, deltaFillPrice, lotSize);

        if (position is null)
        {
            position = new LivePosition { Id = Guid.NewGuid(), InstrumentId = order.InstrumentId };
            dbContext.LivePositions.Add(position);
        }

        var wasFlat = currentNetQuantity == 0;
        var flipped = !wasFlat && update.NewNetQuantity != 0
            && Math.Sign(currentNetQuantity) != Math.Sign(update.NewNetQuantity);

        if (wasFlat || flipped)
        {
            // Fresh open in a new direction - brand-new row, an existing-but-flat row being
            // reopened, or a fill that flipped long<->short outright. Either way, adopt this
            // order's override; averaging into an already-open position (same sign before and
            // after) never changes it.
            position.StopLossPercent = order.StopLossPercent;
            position.TakeProfitPercent = order.TakeProfitPercent;
        }
        else if (update.NewNetQuantity == 0)
        {
            // Fully closed - clear it so a flat row never shows a stale override from the cycle
            // that just ended; the branch above sets it fresh on the next open.
            position.StopLossPercent = null;
            position.TakeProfitPercent = null;
        }

        position.NetQuantity = update.NewNetQuantity;
        position.AverageEntryPrice = update.NewAverageEntryPrice;
        position.RealizedPnL += update.RealizedPnLDelta;
        position.LastUpdatedAtUtc = now;
    }

    private static LiveOrderStatus MapStatus(string kiteStatus) => kiteStatus switch
    {
        Constants.OrderStatus.Complete => LiveOrderStatus.Complete,
        Constants.OrderStatus.Cancelled => LiveOrderStatus.Cancelled,
        Constants.OrderStatus.Rejected => LiveOrderStatus.Failed,
        _ => LiveOrderStatus.Open
    };
}
