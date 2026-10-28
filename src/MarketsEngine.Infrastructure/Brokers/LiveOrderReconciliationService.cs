using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Infrastructure.Health;
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
///
/// This is the guaranteed fallback - KiteTickerService additionally applies the same updates
/// instantly via Kite's push-based Ticker.OnOrderUpdate event when the ticker is running, but the
/// ticker is manually started/stopped and isn't always on, so this poll is what guarantees order
/// status is never permanently stuck even if the ticker was never started. Both paths call the
/// same LiveOrderReconciler.ApplyUpdateAsync and are idempotent against each other.
///
/// Also periodically compares our self-reconciled LivePosition rows against Kite's own real
/// positions/holdings API - LivePosition is normally built purely from our own recorded fills
/// (never checked against the broker), so this is a defense-in-depth drift check: it only logs a
/// warning on mismatch, it never auto-corrects our data from Kite's, since silently overwriting
/// could mask a real bug instead of surfacing it.
/// </summary>
public class LiveOrderReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<LiveTradingSettings> settingsMonitor,
    HealthStatusTracker healthTracker,
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

            healthTracker.RecordHeartbeat(nameof(LiveOrderReconciliationService));

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
        await ReconcilePositionsAgainstKiteAsync(dbContext, kiteClientFactory, cancellationToken);
    }

    private async Task ReconcilePositionsAgainstKiteAsync(
        MarketsEngineDbContext dbContext, KiteClientFactory kiteClientFactory, CancellationToken cancellationToken)
    {
        var ourPositions = await dbContext.LivePositions
            .Include(p => p.Instrument)
            .Where(p => p.NetQuantity != 0)
            .ToListAsync(cancellationToken);

        if (ourPositions.Count == 0)
        {
            return;
        }

        var kite = await kiteClientFactory.CreateAuthenticatedClientAsync(cancellationToken);
        var brokerPositionsByToken = kite.GetPositions().Net
            .GroupBy(p => (long)p.InstrumentToken)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var position in ourPositions)
        {
            var instrumentToken = position.Instrument?.KiteInstrumentToken;
            if (instrumentToken is null)
            {
                continue;
            }

            if (!brokerPositionsByToken.TryGetValue(instrumentToken.Value, out var brokerPosition))
            {
                logger.LogWarning(
                    "Position drift: our LivePosition for {Symbol} shows {OurQuantity} lots, but Kite reports no open position at all.",
                    position.Instrument!.TradingSymbol, position.NetQuantity);
                continue;
            }

            var lotSize = position.Instrument!.LotSize;
            var brokerNetLots = lotSize == 0 ? 0 : brokerPosition.Quantity / lotSize;

            if (brokerNetLots != position.NetQuantity)
            {
                logger.LogWarning(
                    "Position drift: our LivePosition for {Symbol} shows {OurQuantity} lots, Kite reports {BrokerQuantity} lots (avg price ours {OurAvg:F2} vs Kite {BrokerAvg:F2}). Not auto-corrected - review manually.",
                    position.Instrument.TradingSymbol, position.NetQuantity, brokerNetLots,
                    position.AverageEntryPrice, brokerPosition.AveragePrice);
            }
        }
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
            .Include(o => o.Fill)
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

            await LiveOrderReconciler.ApplyUpdateAsync(dbContext, order, brokerOrder, now, logger, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
