using MarketsEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Api.Services;

/// <summary>
/// Dev-only: re-stamps mock quotes with a small random price jitter every few seconds so they
/// don't go permanently stale between manual test calls. Only ever touches DataSource == "Mock"
/// rows, so a real Kite Connect feed (Phase 4) is never affected by this.
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

        var latestQuotesPerInstrument = await dbContext.MarketQuotes
            .Where(q => q.DataSource == "Mock")
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
