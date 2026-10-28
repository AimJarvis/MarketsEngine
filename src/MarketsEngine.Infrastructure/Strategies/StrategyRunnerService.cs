using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.LiveTrading;
using MarketsEngine.Domain.Strategies;
using MarketsEngine.Domain.Trading;
using MarketsEngine.Infrastructure.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Infrastructure.Strategies;

/// <summary>
/// Each pass does two independent things:
/// 1. Exit check - every open position (paper AND live) is checked against the configured
///    take-profit/stop-loss thresholds, regardless of InstrumentKindFilter or ActiveStrategy.
///    An exit always closes in the SAME domain the position was opened in (PaperPosition closes
///    via IOrderExecutionService, LivePosition via ILiveOrderExecutionService) - it never routes
///    through ITradingOrderGateway, because the current LiveTrading:Enabled switch must not
///    decide how an *existing* position gets closed (flipping the switch while an old paper
///    position is still open must never cause its exit to fire a real Kite order).
/// 2. Entry check - the configured IStrategy (keyed by Strategy:ActiveStrategy) is evaluated
///    against every eligible instrument and a buy is submitted through ITradingOrderGateway,
///    which alone decides paper vs. live for a brand-new position based on LiveTrading:Enabled.
/// An instrument is skipped for entry for CooldownMinutes after it was last exited, so a
/// take-profit/stop-loss close can't be immediately undone by an entry signal reacting to the
/// very same price move in the same pass (observed live: exit sold at a profit, then the
/// momentum strategy saw the same spike and immediately bought back in at a worse price).
/// Off by default (Strategy:Enabled).
/// </summary>
public class StrategyRunnerService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<StrategySettings> settingsMonitor,
    HealthStatusTracker healthTracker,
    ILogger<StrategyRunnerService> logger) : BackgroundService
{
    private readonly Dictionary<Guid, Queue<PricePoint>> _priceHistory = new();
    private readonly Dictionary<Guid, DateTime> _lastExitAtUtc = new();
    private readonly Lock _historyLock = new();

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
                    logger.LogError(ex, "Strategy evaluation pass failed");
                }
            }

            healthTracker.RecordHeartbeat(nameof(StrategyRunnerService));

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.EvaluationIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    public async Task RunOnceAsync(StrategySettings settings, CancellationToken cancellationToken)
    {
        if (settings.AccountId is null)
        {
            logger.LogWarning("Strategy runner is enabled but Strategy:AccountId is not configured - skipping");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();
        var orderExecutionService = scope.ServiceProvider.GetRequiredService<IOrderExecutionService>();
        var liveOrderExecutionService = scope.ServiceProvider.GetRequiredService<ILiveOrderExecutionService>();
        var tradingOrderGateway = scope.ServiceProvider.GetRequiredService<ITradingOrderGateway>();
        var strategy = scope.ServiceProvider.GetRequiredKeyedService<IStrategy>(settings.ActiveStrategy);

        var now = DateTime.UtcNow;

        await CheckPaperExitsAsync(settings, dbContext, orderExecutionService, now, cancellationToken);
        await CheckLiveExitsAsync(settings, dbContext, liveOrderExecutionService, now, cancellationToken);

        var today = DateOnly.FromDateTime(now);

        var instrumentsQuery = dbContext.Instruments
            .Where(i => i.ExpiryDate == null || i.ExpiryDate >= today);

        instrumentsQuery = settings.InstrumentKindFilter switch
        {
            "Option" => instrumentsQuery.Where(i => i.Kind == InstrumentKind.Option),
            "Equity" => instrumentsQuery.Where(i => i.Kind == InstrumentKind.Equity),
            _ => instrumentsQuery
        };

        var instruments = await instrumentsQuery.ToListAsync(cancellationToken);

        foreach (var instrument in instruments)
        {
            try
            {
                lock (_historyLock)
                {
                    if (_lastExitAtUtc.TryGetValue(instrument.Id, out var lastExit)
                        && now - lastExit < TimeSpan.FromMinutes(settings.CooldownMinutes))
                    {
                        continue;
                    }
                }

                var latestQuote = await dbContext.MarketQuotes
                    .Where(q => q.InstrumentId == instrument.Id)
                    .OrderByDescending(q => q.QuotedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (latestQuote is null)
                {
                    continue;
                }

                bool hasHistory;
                lock (_historyLock)
                {
                    hasHistory = _priceHistory.ContainsKey(instrument.Id);
                }

                if (!hasHistory)
                {
                    // First time seeing this instrument since the app started - seed the rolling
                    // window from already-persisted quotes instead of starting empty, so a restart
                    // doesn't silently reset the strategy's view of recent price action.
                    var seedQuotes = await dbContext.MarketQuotes
                        .Where(q => q.InstrumentId == instrument.Id)
                        .OrderByDescending(q => q.QuotedAtUtc)
                        .Take(settings.LookbackSamples)
                        .ToListAsync(cancellationToken);

                    var seeded = new Queue<PricePoint>(
                        seedQuotes.OrderBy(q => q.QuotedAtUtc).Select(q => new PricePoint(q.QuotedAtUtc, q.LastTradedPrice)));

                    lock (_historyLock)
                    {
                        if (!_priceHistory.ContainsKey(instrument.Id))
                        {
                            _priceHistory[instrument.Id] = seeded;
                        }
                    }
                }

                PricePoint[] windowSnapshot;
                lock (_historyLock)
                {
                    var history = _priceHistory.TryGetValue(instrument.Id, out var existing)
                        ? existing
                        : _priceHistory[instrument.Id] = new Queue<PricePoint>();

                    if (history.Count == 0 || history.Last().TimestampUtc != latestQuote.QuotedAtUtc)
                    {
                        history.Enqueue(new PricePoint(latestQuote.QuotedAtUtc, latestQuote.LastTradedPrice));
                    }

                    while (history.Count > settings.LookbackSamples)
                    {
                        history.Dequeue();
                    }

                    windowSnapshot = history.ToArray();
                }

                var context = new StrategyContext(instrument.Id, instrument.TradingSymbol, windowSnapshot);
                var signal = await strategy.EvaluateAsync(context, cancellationToken);

                if (!signal.ShouldBuy)
                {
                    continue;
                }

                var cooldownBucket = RoundDownToInterval(now, settings.CooldownMinutes);
                var idempotencyKey = $"strategy:{settings.ActiveStrategy}:{instrument.Id}:{cooldownBucket:yyyyMMddHHmm}";

                var request = new GatewayOrderRequest(
                    settings.AccountId.Value, instrument.Id, OrderSide.Buy, TradingStyle.Intraday,
                    settings.QuantityPerSignal, idempotencyKey, $"Strategy:{settings.ActiveStrategy}");

                var result = await tradingOrderGateway.SubmitAsync(request, cancellationToken);

                if (result.Success && !result.PendingApproval)
                {
                    logger.LogInformation(
                        "Strategy '{Strategy}' bought {Quantity} of {Symbol} in {Mode} mode: {Reason}",
                        settings.ActiveStrategy, settings.QuantityPerSignal, instrument.TradingSymbol, result.Mode, signal.Reason);
                }
                else if (result.Success && result.PendingApproval)
                {
                    logger.LogInformation(
                        "Strategy '{Strategy}' signaled buy on {Symbol} - live order {OrderId} parked for manual approval",
                        settings.ActiveStrategy, instrument.TradingSymbol, result.OrderId);
                }
                else
                {
                    logger.LogInformation(
                        "Strategy '{Strategy}' signaled buy on {Symbol} but order was rejected: {ErrorCode} - {ErrorMessage}",
                        settings.ActiveStrategy, instrument.TradingSymbol, result.ErrorCode, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Strategy evaluation failed for instrument {InstrumentId}", instrument.Id);
            }
        }
    }

    private async Task CheckPaperExitsAsync(
        StrategySettings settings, MarketsEngineDbContext dbContext, IOrderExecutionService orderExecutionService,
        DateTime now, CancellationToken cancellationToken)
    {
        var positions = await dbContext.PaperPositions
            .Where(p => p.AccountId == settings.AccountId!.Value && p.NetQuantity != 0)
            .Include(p => p.Instrument)
            .ToListAsync(cancellationToken);

        foreach (var position in positions)
        {
            try
            {
                var latestQuote = await dbContext.MarketQuotes
                    .Where(q => q.InstrumentId == position.InstrumentId)
                    .OrderByDescending(q => q.QuotedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (latestQuote is null)
                {
                    continue;
                }

                var exitReason = PositionExitEvaluator.Evaluate(
                    position.NetQuantity, position.AverageEntryPrice, latestQuote.LastTradedPrice,
                    position.Instrument!.LotSize, settings.TakeProfitPercent, settings.StopLossPercent);

                if (exitReason == PositionExitEvaluator.ExitReason.None)
                {
                    continue;
                }

                var closingSide = position.NetQuantity > 0 ? OrderSide.Sell : OrderSide.Buy;
                var closingQuantity = Math.Abs(position.NetQuantity);
                var idempotencyKey = $"exit:{exitReason}:{position.InstrumentId}:{now:yyyyMMddHHmmssfff}";

                var request = new SubmitOrderRequest(
                    settings.AccountId!.Value, position.InstrumentId, closingSide, TradingStyle.Intraday,
                    closingQuantity, idempotencyKey);

                var result = await orderExecutionService.SubmitOrderAsync(request, cancellationToken);

                if (result.Success)
                {
                    lock (_historyLock)
                    {
                        _lastExitAtUtc[position.InstrumentId] = now;
                    }

                    logger.LogInformation(
                        "{ExitReason} closed {Quantity} of {Symbol} (paper) via {Side}",
                        exitReason, closingQuantity, position.Instrument.TradingSymbol, closingSide);
                }
                else
                {
                    logger.LogInformation(
                        "{ExitReason} triggered on {Symbol} (paper) but closing order was rejected: {ErrorCode} - {ErrorMessage}",
                        exitReason, position.Instrument.TradingSymbol, result.ErrorCode, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper exit check failed for position {PositionId}", position.Id);
            }
        }
    }

    private async Task CheckLiveExitsAsync(
        StrategySettings settings, MarketsEngineDbContext dbContext, ILiveOrderExecutionService liveOrderExecutionService,
        DateTime now, CancellationToken cancellationToken)
    {
        var positions = await dbContext.LivePositions
            .Where(p => p.NetQuantity != 0)
            .Include(p => p.Instrument)
            .ToListAsync(cancellationToken);

        foreach (var position in positions)
        {
            try
            {
                var latestQuote = await dbContext.MarketQuotes
                    .Where(q => q.InstrumentId == position.InstrumentId)
                    .OrderByDescending(q => q.QuotedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (latestQuote is null)
                {
                    continue;
                }

                var exitReason = PositionExitEvaluator.Evaluate(
                    position.NetQuantity, position.AverageEntryPrice, latestQuote.LastTradedPrice,
                    position.Instrument!.LotSize, settings.TakeProfitPercent, settings.StopLossPercent);

                if (exitReason == PositionExitEvaluator.ExitReason.None)
                {
                    continue;
                }

                var closingSide = position.NetQuantity > 0 ? OrderSide.Sell : OrderSide.Buy;
                var closingQuantity = Math.Abs(position.NetQuantity);
                var idempotencyKey = $"live-exit:{exitReason}:{position.InstrumentId}:{now:yyyyMMddHHmmssfff}";

                var request = new SubmitLiveOrderRequest(
                    position.InstrumentId, closingSide, closingQuantity, idempotencyKey, $"Exit:{exitReason}");

                var result = await liveOrderExecutionService.SubmitOrderAsync(request, cancellationToken);

                if (result.Success)
                {
                    lock (_historyLock)
                    {
                        _lastExitAtUtc[position.InstrumentId] = now;
                    }

                    if (result.PendingApproval)
                    {
                        logger.LogInformation(
                            "{ExitReason} on {Symbol} (live) parked order {OrderId} for manual approval",
                            exitReason, position.Instrument!.TradingSymbol, result.Order!.Id);
                    }
                    else
                    {
                        logger.LogInformation(
                            "{ExitReason} closed {Quantity} of {Symbol} (live) via {Side}",
                            exitReason, closingQuantity, position.Instrument!.TradingSymbol, closingSide);
                    }
                }
                else
                {
                    logger.LogInformation(
                        "{ExitReason} triggered on {Symbol} (live) but order was rejected: {ErrorCode} - {ErrorMessage}",
                        exitReason, position.Instrument!.TradingSymbol, result.ErrorCode, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live exit check failed for position {PositionId}", position.Id);
            }
        }
    }

    private static DateTime RoundDownToInterval(DateTime value, int minutes)
    {
        var ticksPerInterval = TimeSpan.FromMinutes(minutes).Ticks;
        return new DateTime(value.Ticks - (value.Ticks % ticksPerInterval), value.Kind);
    }
}
