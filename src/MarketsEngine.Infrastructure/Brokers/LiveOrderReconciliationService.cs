using KiteConnect;
using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Polls Kite's real order book for every LiveOrder we've submitted but haven't seen reach a
/// terminal state yet, since Kite's PlaceOrder returns an order id immediately but the order
/// itself fills asynchronously. Also expires stale PendingApproval orders so an approval can
/// never fire against a long-stale price. Off unless LiveTrading:Enabled.
/// </summary>
public class LiveOrderReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<LiveTradingSettings> settingsMonitor,
    ILogger<LiveOrderReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = settingsMonitor.CurrentValue;

            if (settings.Enabled)
            {
                try
                {
                    await RunOnceAsync(settings, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Live order reconciliation pass failed");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.ReconciliationIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    public async Task RunOnceAsync(LiveTradingSettings settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();
        var kiteClientFactory = scope.ServiceProvider.GetRequiredService<KiteClientFactory>();

        var now = DateTime.UtcNow;

        await ExpireStalePendingApprovalsAsync(dbContext, settings, now, cancellationToken);
        await ReconcileOpenOrdersAsync(dbContext, kiteClientFactory, now, cancellationToken);
    }

    private async Task ExpireStalePendingApprovalsAsync(
        MarketsEngineDbContext dbContext, LiveTradingSettings settings, DateTime now, CancellationToken cancellationToken)
    {
        var expiryCutoff = now.AddMinutes(-settings.PendingApprovalExpiryMinutes);

        var stale = await dbContext.LiveOrders
            .Where(o => o.Status == LiveOrderStatus.PendingApproval && o.RequestedAtUtc < expiryCutoff)
            .ToListAsync(cancellationToken);

        foreach (var order in stale)
        {
            order.Status = LiveOrderStatus.Rejected;
            order.RejectionReason = $"Approval window of {settings.PendingApprovalExpiryMinutes} minutes expired without action.";
            logger.LogInformation("Live order {OrderId} auto-rejected: approval window expired", order.Id);
        }

        if (stale.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ReconcileOpenOrdersAsync(
        MarketsEngineDbContext dbContext, KiteClientFactory kiteClientFactory, DateTime now, CancellationToken cancellationToken)
    {
        var openOrders = await dbContext.LiveOrders
            .Include(o => o.Instrument)
            .Where(o => o.Status == LiveOrderStatus.Submitted || o.Status == LiveOrderStatus.Open || o.Status == LiveOrderStatus.PartiallyFilled)
            .ToListAsync(cancellationToken);

        if (openOrders.Count == 0)
        {
            return;
        }

        var kite = await kiteClientFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var brokerOrders = kite.GetOrders().ToDictionary(o => o.OrderId);

        foreach (var order in openOrders)
        {
            if (order.BrokerOrderId is null || !brokerOrders.TryGetValue(order.BrokerOrderId, out var brokerOrder))
            {
                continue;
            }

            var newStatus = MapStatus(brokerOrder.Status);
            if (newStatus == order.Status)
            {
                continue;
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

        await dbContext.SaveChangesAsync(cancellationToken);
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
