using System.Collections.Concurrent;
using KiteConnect;
using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Owns a single Kite WebSocket ticker subscription and writes ticks into the matching
/// MarketQuote row (DataSource="Kite", updated in place rather than appended, mirroring how
/// the mock quote refresher behaves). Read-only market data - never touches order placement.
/// Registered as a singleton since Connect() manages its own background connection; a scope
/// is opened per DB access since DbContext itself is scoped.
///
/// OnTick never touches the DB directly - Kite can fire ticks for hundreds of subscribed
/// instruments many times a second (observed live: subscribing to 1000+ NIFTY/BANKNIFTY option
/// contracts in TickerMode.Full exhausted Postgres's entire connection pool within seconds, back
/// when this opened one scope+connection per tick, fire-and-forget, with no throttling at all).
/// Ticks are instead buffered in memory (last tick per instrument wins) and flushed to the DB in
/// one batch every FlushInterval, bounding DB load to one connection every few seconds regardless
/// of how fast ticks actually arrive.
///
/// Also applies real-time order status updates pushed on this same WebSocket connection
/// (Ticker.OnOrderUpdate) to any matching LiveOrder, via the shared LiveOrderReconciler - this
/// is the low-latency path; LiveOrderReconciliationService's periodic poll is the guaranteed
/// fallback for whenever this ticker isn't running (it's started/stopped manually). Order updates
/// are rare compared to price ticks, so they're still applied individually, not batched.
/// </summary>
public class KiteTickerService(IServiceScopeFactory scopeFactory, ILogger<KiteTickerService> logger) : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private Ticker? _ticker;
    private Timer? _flushTimer;
    private readonly ConcurrentDictionary<uint, Tick> _pendingTicks = new();
    private int _flushInProgress;

    public bool IsConnected => _ticker?.IsConnected ?? false;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Stop();

        using var scope = scopeFactory.CreateScope();
        var clientFactory = scope.ServiceProvider.GetRequiredService<KiteClientFactory>();
        var sessionStore = scope.ServiceProvider.GetRequiredService<KiteSessionStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();

        var session = await sessionStore.GetAsync(cancellationToken)
            ?? throw new KiteNotConfiguredException("No Kite session found. Complete the login flow first: GET /api/broker/kite/login-url");

        var tokens = await dbContext.Instruments
            .Where(i => i.DataSource == "Kite" && i.KiteInstrumentToken != null)
            .Select(i => (uint)i.KiteInstrumentToken!.Value)
            .ToArrayAsync(cancellationToken);

        if (tokens.Length == 0)
        {
            throw new InvalidOperationException(
                "No Kite instruments to subscribe to. Sync instruments first: POST /api/broker/kite/sync-instruments");
        }

        _ticker = new Ticker(clientFactory.ApiKey, session.AccessToken, Reconnect: true, ReconnectInterval: 5, ReconnectTries: 50);
        _ticker.OnTick += OnTick;
        _ticker.OnOrderUpdate += OnOrderUpdate;
        _ticker.OnError += message => logger.LogWarning("Kite ticker error: {Message}", message);
        _ticker.OnClose += () => logger.LogInformation("Kite ticker closed");
        _ticker.OnConnect += () => logger.LogInformation("Kite ticker connected");
        _ticker.OnReconnect += () => logger.LogInformation("Kite ticker reconnecting");
        _ticker.OnNoReconnect += () => logger.LogWarning("Kite ticker gave up reconnecting after repeated failures");

        _ticker.Connect();
        _ticker.Subscribe(tokens);
        _ticker.SetMode(tokens, Constants.TickerMode.Full);

        _flushTimer = new Timer(_ => FlushPendingTicks(), null, FlushInterval, FlushInterval);

        logger.LogInformation("Kite ticker started, subscribed to {Count} instruments", tokens.Length);
    }

    public void Stop()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        _pendingTicks.Clear();

        if (_ticker is null)
        {
            return;
        }

        _ticker.OnTick -= OnTick;
        _ticker.OnOrderUpdate -= OnOrderUpdate;
        _ticker.Close();
        _ticker = null;
    }

    private void OnTick(Tick tick)
    {
        // Cheap, synchronous, in-memory only - no DB access on the hot path. Last tick per
        // instrument wins; FlushPendingTicksAsync drains this on its own schedule.
        _pendingTicks[tick.InstrumentToken] = tick;
    }

    private void OnOrderUpdate(Order orderData)
    {
        _ = HandleOrderUpdateAsync(orderData);
    }

    private async Task HandleOrderUpdateAsync(Order orderData)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();

            var order = await dbContext.LiveOrders
                .Include(o => o.Instrument)
                .Include(o => o.Fills)
                .FirstOrDefaultAsync(o => o.BrokerOrderId == orderData.OrderId);

            if (order is null)
            {
                return;
            }

            await LiveOrderReconciler.ApplyUpdateAsync(dbContext, order, orderData, DateTime.UtcNow, logger);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process Kite order update for broker order {BrokerOrderId}", orderData.OrderId);
        }
    }

    private void FlushPendingTicks()
    {
        // Re-entrancy guard: if a flush from the previous timer tick is still running (DB slow,
        // large batch, etc.), skip this one rather than stacking up overlapping flush tasks - the
        // next cycle will simply pick up whatever accumulated in the meantime.
        if (Interlocked.Exchange(ref _flushInProgress, 1) == 1)
        {
            return;
        }

        _ = FlushPendingTicksAsync();
    }

    private async Task FlushPendingTicksAsync()
    {
        try
        {
            if (_pendingTicks.IsEmpty)
            {
                return;
            }

            var batch = new Dictionary<uint, Tick>();
            foreach (var token in _pendingTicks.Keys.ToArray())
            {
                if (_pendingTicks.TryRemove(token, out var tick))
                {
                    batch[token] = tick;
                }
            }

            if (batch.Count == 0)
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();

            var tokens = batch.Keys.Select(t => (long)t).ToArray();

            var instruments = await dbContext.Instruments
                .Where(i => i.KiteInstrumentToken != null && tokens.Contains(i.KiteInstrumentToken.Value))
                .ToListAsync();

            if (instruments.Count == 0)
            {
                return;
            }

            var instrumentIds = instruments.Select(i => i.Id).ToArray();

            var latestQuoteByInstrumentId = (await dbContext.MarketQuotes
                    .Where(q => instrumentIds.Contains(q.InstrumentId))
                    .ToListAsync())
                .GroupBy(q => q.InstrumentId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.QuotedAtUtc).First());

            var now = DateTime.UtcNow;

            foreach (var instrument in instruments)
            {
                if (!batch.TryGetValue((uint)instrument.KiteInstrumentToken!.Value, out var tick))
                {
                    continue;
                }

                if (!latestQuoteByInstrumentId.TryGetValue(instrument.Id, out var quote))
                {
                    quote = new MarketQuote { Id = Guid.NewGuid(), InstrumentId = instrument.Id, DataSource = "Kite" };
                    dbContext.MarketQuotes.Add(quote);
                }

                var (bidPrice, askPrice) = ResolveBidAsk(tick);
                quote.BidPrice = bidPrice;
                quote.AskPrice = askPrice;
                quote.LastTradedPrice = tick.LastPrice;
                quote.DataSource = "Kite";
                quote.QuotedAtUtc = ToUtc(tick.Timestamp) ?? now;
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush pending Kite ticks");
        }
        finally
        {
            Volatile.Write(ref _flushInProgress, 0);
        }
    }

    /// <summary>
    /// Kite's own SDK (Utils.UnixToDateTime) builds Tick.Timestamp by adding the IST offset to the
    /// Unix epoch before adding elapsed seconds - so the resulting value is already IST wall-clock
    /// time, just tagged DateTimeKind.Unspecified (confirmed by reading the SDK source directly,
    /// not assumed). Npgsql refuses to write an Unspecified-kind DateTime into a "timestamp with
    /// time zone" column at all, so this must both subtract the IST offset AND mark the result
    /// Utc - relabeling it Utc without subtracting would silently shift every quote 5.5 hours.
    /// </summary>
    private static DateTime? ToUtc(DateTime? kiteIstTimestamp) =>
        kiteIstTimestamp.HasValue
            ? DateTime.SpecifyKind(kiteIstTimestamp.Value.AddHours(-5.5), DateTimeKind.Utc)
            : null;

    /// <summary>
    /// Ticker.cs (the SDK itself) always allocates Bids/Offers as a fixed `new DepthItem[5]` in
    /// Full mode, regardless of real liquidity - an empty depth level is Price=0/Quantity=0, NOT
    /// a shorter array (confirmed by reading the SDK source directly, not assumed). So checking
    /// `Length > 0` is always true and never actually detects "no real bid/offer" - the real
    /// check is on the price value itself. Many of the synced NIFTY/BANKNIFTY option contracts are
    /// illiquid (deep OTM or far expiry) and genuinely have an empty top-of-book level, and
    /// LastPrice can be stale relative to whatever depth does exist - resolving bid/ask
    /// independently per side can therefore produce ask &lt; bid, which violated this table's
    /// CK_MarketQuote_Ask_Gte_Bid check constraint on real data. Only trust the real book when
    /// BOTH sides have a genuine non-zero price and aren't crossed; otherwise collapse to
    /// LastPrice for both, same as the mock quote generator already does for a single-number quote.
    /// </summary>
    private static (decimal BidPrice, decimal AskPrice) ResolveBidAsk(Tick tick)
    {
        if (tick.Bids is { Length: > 0 } && tick.Offers is { Length: > 0 })
        {
            var bid = tick.Bids[0].Price;
            var ask = tick.Offers[0].Price;
            if (bid > 0 && ask > 0 && ask >= bid)
            {
                return (bid, ask);
            }
        }

        return (tick.LastPrice, tick.LastPrice);
    }

    public void Dispose() => Stop();
}
