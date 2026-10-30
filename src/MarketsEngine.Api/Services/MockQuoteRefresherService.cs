using MarketsEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Api.Services;

/// <summary>
/// Dev-only: re-stamps mock quotes with a small random price jitter every few seconds so they
/// don't go permanently stale between manual test calls. Only ever touches instruments whose
/// Instrument.DataSource is still "Mock" right now - not just quote rows tagged "Mock" - so an
/// instrument that gets synced/upgraded to real Kite data stops being touched immediately, even
/// if old mock MarketQuote rows for it still exist in history (KiteInstrumentSyncService upgrades
/// a matching mock Instrument in place rather than creating a second row for the same symbol).
/// </summary>
public class MockQuoteRefresherService(IServiceScopeFactory scopeFactory, ILogger<MockQuoteRefresherService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshMockQuotesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh mock quotes");
            }
        }
    }

    private async Task RefreshMockQuotesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();

        var mockInstrumentIds = await dbContext.Instruments
            .Where(i => i.DataSource == "Mock")
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        var latestQuotesPerInstrument = await dbContext.MarketQuotes
            .Where(q => q.DataSource == "Mock" && mockInstrumentIds.Contains(q.InstrumentId))
            .GroupBy(q => q.InstrumentId)
            .Select(g => g.OrderByDescending(q => q.QuotedAtUtc).First())
            .ToListAsync(cancellationToken);

        if (latestQuotesPerInstrument.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var quote in latestQuotesPerInstrument)
        {
            var jitter = 1 + ((decimal)(_random.NextDouble() - 0.5) * 0.02m);
            var newLtp = Math.Max(0.05m, Math.Round(quote.LastTradedPrice * jitter, 2));
            var spread = Math.Max(0.5m, Math.Round(newLtp * 0.01m, 2));

            quote.BidPrice = Math.Max(0.05m, newLtp - spread / 2);
            quote.AskPrice = quote.BidPrice + spread;
            quote.LastTradedPrice = newLtp;
            quote.QuotedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
