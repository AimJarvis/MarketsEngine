using KiteConnect;
using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Applies one Kite order update (status + fill) to a LiveOrder/LiveFill/LivePosition. Shared by
/// two independent paths that can both observe the same real-world event: LiveOrderReconciliationService
/// polling kite.GetOrders() as a guaranteed fallback, and KiteTickerService's push-based
/// Ticker.OnOrderUpdate handler for low latency when the ticker is running. Idempotent - applying
/// the same brokerOrder twice is a no-op if the status hasn't changed, so having both paths active
/// at once is safe, not a race.
/// </summary>
public static class LiveOrderReconciler
{
    /// <summary>order must already have .Fill and .Instrument loaded by the caller.</summary>
    public static async Task ApplyUpdateAsync(
        MarketsEngineDbContext dbContext, LiveOrder order, Order brokerOrder, DateTime now, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var newStatus = MapStatus(brokerOrder.Status);
        if (newStatus == order.Status)
        {
            return;
        }

        var wasAlreadyFilled = order.Fill is not null;
        order.Status = newStatus;

        if (newStatus == LiveOrderStatus.Complete && !wasAlreadyFilled)
        {
            await RecordFillAsync(dbContext, order, brokerOrder, now, cancellationToken);
        }
        else if (newStatus is LiveOrderStatus.Cancelled)
        {
            order.RejectionReason = brokerOrder.StatusMessage;
        }

        logger.LogInformation("Live order {OrderId} ({BrokerOrderId}) status updated to {Status}", order.Id, order.BrokerOrderId, newStatus);
    }

    private static async Task RecordFillAsync(
        MarketsEngineDbContext dbContext, LiveOrder order, Order brokerOrder, DateTime now, CancellationToken cancellationToken)
    {
        var fill = new LiveFill
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FilledQuantity = (int)brokerOrder.FilledQuantity,
            FillPrice = brokerOrder.AveragePrice,
            FilledAtUtc = now
        };

        dbContext.LiveFills.Add(fill);

        var position = await dbContext.LivePositions
            .FirstOrDefaultAsync(p => p.InstrumentId == order.InstrumentId, cancellationToken);

        var lotSize = order.Instrument?.LotSize ?? 1;
        var currentNetQuantity = position?.NetQuantity ?? 0;
        var currentAveragePrice = position?.AverageEntryPrice ?? 0m;

        var lots = order.Quantity;
        var update = PositionUpdater.ApplyFill(currentNetQuantity, currentAveragePrice, order.Side, lots, fill.FillPrice, lotSize);

        if (position is null)
        {
            position = new LivePosition { Id = Guid.NewGuid(), InstrumentId = order.InstrumentId };
            dbContext.LivePositions.Add(position);
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
