using KiteConnect;
using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Owns a single Kite WebSocket ticker subscription and writes each tick into the matching
/// MarketQuote row (DataSource="Kite", updated in place rather than appended, mirroring how
/// the mock quote refresher behaves). Read-only market data - never touches order placement.
/// Registered as a singleton since Connect() manages its own background connection; a scope
/// is opened per DB access since DbContext itself is scoped.
/// </summary>
public class KiteTickerService(IServiceScopeFactory scopeFactory, ILogger<KiteTickerService> logger) : IDisposable
{
    private Ticker? _ticker;

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
        _ticker.OnError += message => logger.LogWarning("Kite ticker error: {Message}", message);
        _ticker.OnClose += () => logger.LogInformation("Kite ticker closed");
        _ticker.OnConnect += () => logger.LogInformation("Kite ticker connected");
        _ticker.OnReconnect += () => logger.LogInformation("Kite ticker reconnecting");
        _ticker.OnNoReconnect += () => logger.LogWarning("Kite ticker gave up reconnecting after repeated failures");

        _ticker.Connect();
        _ticker.Subscribe(tokens);
        _ticker.SetMode(tokens, Constants.TickerMode.Full);

        logger.LogInformation("Kite ticker started, subscribed to {Count} instruments", tokens.Length);
    }

    public void Stop()
    {
        if (_ticker is null)
        {
            return;
        }

        _ticker.OnTick -= OnTick;
        _ticker.Close();
        _ticker = null;
    }

    private void OnTick(Tick tick)
    {
        _ = UpdateQuoteAsync(tick);
    }

    private async Task UpdateQuoteAsync(Tick tick)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();

            var instrument = await dbContext.Instruments
                .FirstOrDefaultAsync(i => i.KiteInstrumentToken == tick.InstrumentToken);

            if (instrument is null)
            {
                return;
            }

            var quote = await dbContext.MarketQuotes
                .Where(q => q.InstrumentId == instrument.Id)
                .OrderByDescending(q => q.QuotedAtUtc)
                .FirstOrDefaultAsync();

            if (quote is null)
            {
                quote = new MarketQuote { Id = Guid.NewGuid(), InstrumentId = instrument.Id, DataSource = "Kite" };
                dbContext.MarketQuotes.Add(quote);
            }

            quote.BidPrice = tick.Bids is { Length: > 0 } ? tick.Bids[0].Price : tick.LastPrice;
            quote.AskPrice = tick.Offers is { Length: > 0 } ? tick.Offers[0].Price : tick.LastPrice;
            quote.LastTradedPrice = tick.LastPrice;
            quote.DataSource = "Kite";
            quote.QuotedAtUtc = tick.Timestamp ?? DateTime.UtcNow;

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Kite tick for instrument token {Token}", tick.InstrumentToken);
        }
    }

    public void Dispose() => Stop();
}
