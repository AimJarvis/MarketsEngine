using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Trading;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Infrastructure;

/// <summary>
/// Shared P&amp;L aggregation, used by both AccountsController (on-demand queries) and
/// PnLAlertService (periodic WhatsApp alerts) so the calculation only lives in one place.
/// </summary>
public class AccountPnLService(MarketsEngineDbContext dbContext)
{
    public async Task<decimal?> CalculateUnrealizedPnLAsync(PaperPosition position, CancellationToken cancellationToken = default)
    {
        var latestQuote = await dbContext.MarketQuotes
            .Where(q => q.InstrumentId == position.InstrumentId)
            .OrderByDescending(q => q.QuotedAtUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return latestQuote is null
            ? null
            : PnLCalculator.CalculateUnrealizedPnL(position.NetQuantity, position.AverageEntryPrice, latestQuote.LastTradedPrice, position.Instrument!.LotSize);
    }

    public async Task<(decimal Realized, decimal Unrealized, decimal Net)> GetTotalPnLAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var positions = await dbContext.PaperPositions
            .Where(p => p.AccountId == accountId)
            .Include(p => p.Instrument)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalRealized = positions.Sum(p => p.RealizedPnL);
        var totalUnrealized = 0m;

        foreach (var position in positions.Where(p => p.NetQuantity != 0))
        {
            totalUnrealized += await CalculateUnrealizedPnLAsync(position, cancellationToken) ?? 0m;
        }

        return (totalRealized, totalUnrealized, totalRealized + totalUnrealized);
    }
}
