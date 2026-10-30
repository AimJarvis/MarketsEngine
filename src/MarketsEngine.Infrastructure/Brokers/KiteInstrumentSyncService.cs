using KiteConnect;
using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainInstrument = MarketsEngine.Domain.Entities.Instrument;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Pulls Kite's instrument master and upserts tracked contracts into our own Instruments
/// table, tagged DataSource="Kite" so mock and real instruments never mix. Two independent
/// syncs: NIFTY/BANKNIFTY options (NFO segment, nearest couple of expiries) and a configurable
/// equity watchlist (NSE segment). Read-only: never touches order placement.
/// </summary>
public class KiteInstrumentSyncService(
    KiteClientFactory clientFactory,
    MarketsEngineDbContext dbContext,
    IOptions<KiteConnectSettings> kiteSettingsOptions,
    ILogger<KiteInstrumentSyncService> logger)
{
    private static readonly string[] TrackedUnderlyings = ["NIFTY", "BANKNIFTY"];
    private const int MaxExpiriesPerUnderlying = 2;
    private const string KiteDataSource = "Kite";

    public async Task<int> SyncNiftyBankNiftyOptionsAsync(CancellationToken cancellationToken = default)
    {
        var kite = await clientFactory.CreateAuthenticatedClientAsync(cancellationToken);

        var allNfoInstruments = kite.GetInstruments(Constants.Exchange.NFO);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var candidates = allNfoInstruments
            .Where(i => TrackedUnderlyings.Contains(i.Name)
                        && (i.InstrumentType == "CE" || i.InstrumentType == "PE")
                        && i.Expiry.HasValue
                        && DateOnly.FromDateTime(i.Expiry.Value) >= today)
            .ToList();

        var nearestExpiriesPerUnderlying = candidates
            .GroupBy(i => i.Name)
            .ToDictionary(
                g => g.Key,
                g => g.Select(i => i.Expiry!.Value).Distinct().OrderBy(d => d).Take(MaxExpiriesPerUnderlying).ToHashSet());

        var toUpsert = candidates
            .Where(i => nearestExpiriesPerUnderlying[i.Name].Contains(i.Expiry!.Value))
            .ToList();

        // Matched by TradingSymbol alone, not scoped to DataSource=="Kite" - a mock instrument
        // seeded for Development (same symbol) must be found and upgraded in place, not collide
        // with a duplicate insert against the global unique index on TradingSymbol.
        var existingBySymbol = await dbContext.Instruments
            .Where(i => i.Kind == InstrumentKind.Option)
            .ToDictionaryAsync(i => i.TradingSymbol, cancellationToken);

        var now = DateTime.UtcNow;
        var upsertedCount = 0;

        foreach (var kiteInstrument in toUpsert)
        {
            if (!existingBySymbol.TryGetValue(kiteInstrument.TradingSymbol, out var instrument))
            {
                instrument = new DomainInstrument
                {
                    Id = Guid.NewGuid(),
                    TradingSymbol = kiteInstrument.TradingSymbol,
                    Kind = InstrumentKind.Option,
                    CreatedAtUtc = now
                };
                dbContext.Instruments.Add(instrument);
            }

            // Re-asserted every time (not just on insert) - a matched row may have been a mock
            // instrument a moment ago, and it must be fully upgraded to real, not left tagged Mock
            // while carrying real Kite data (LiveOrderExecutionService gates on this flag).
            instrument.DataSource = KiteDataSource;
            instrument.Underlying = kiteInstrument.Name == "BANKNIFTY" ? Underlying.BankNifty : Underlying.Nifty;
            instrument.OptionType = kiteInstrument.InstrumentType == "CE" ? OptionType.Call : OptionType.Put;
            instrument.StrikePrice = kiteInstrument.Strike;
            instrument.ExpiryDate = DateOnly.FromDateTime(kiteInstrument.Expiry!.Value);
            instrument.LotSize = (int)kiteInstrument.LotSize;
            instrument.KiteInstrumentToken = kiteInstrument.InstrumentToken;

            upsertedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Synced {Count} Kite NIFTY/BANKNIFTY option instruments across {ExpiryCount} expiries",
            upsertedCount, nearestExpiriesPerUnderlying.Values.SelectMany(x => x).Distinct().Count());

        return upsertedCount;
    }

    public async Task<int> SyncEquitiesAsync(CancellationToken cancellationToken = default)
    {
        var watchlist = kiteSettingsOptions.Value.EquityWatchlist;
        if (watchlist.Length == 0)
        {
            logger.LogWarning("Kite:EquityWatchlist is empty - nothing to sync. Add symbols in appsettings.json.");
            return 0;
        }

        var kite = await clientFactory.CreateAuthenticatedClientAsync(cancellationToken);

        var allNseInstruments = kite.GetInstruments(Constants.Exchange.NSE);

        var candidates = allNseInstruments
            .Where(i => i.InstrumentType == "EQ" && watchlist.Contains(i.TradingSymbol))
            .ToList();

        // Matched by TradingSymbol alone, not scoped to DataSource=="Kite" - the Development mock
        // seed uses the exact same symbols as a real equity watchlist on purpose, so a mock row
        // must be found and upgraded in place, not collide with a duplicate insert against the
        // global unique index on TradingSymbol.
        var existingBySymbol = await dbContext.Instruments
            .Where(i => i.Kind == InstrumentKind.Equity)
            .ToDictionaryAsync(i => i.TradingSymbol, cancellationToken);

        var now = DateTime.UtcNow;
        var upsertedCount = 0;

        foreach (var kiteInstrument in candidates)
        {
            if (!existingBySymbol.TryGetValue(kiteInstrument.TradingSymbol, out var instrument))
            {
                instrument = new DomainInstrument
                {
                    Id = Guid.NewGuid(),
                    TradingSymbol = kiteInstrument.TradingSymbol,
                    Kind = InstrumentKind.Equity,
                    CreatedAtUtc = now
                };
                dbContext.Instruments.Add(instrument);
            }

            // Re-asserted every time (not just on insert) - a matched row may have been a mock
            // instrument a moment ago, and it must be fully upgraded to real, not left tagged Mock
            // while carrying real Kite data (LiveOrderExecutionService gates on this flag).
            instrument.DataSource = KiteDataSource;
            instrument.Underlying = null;
            instrument.OptionType = null;
            instrument.StrikePrice = 0m;
            instrument.ExpiryDate = null;
            instrument.LotSize = 1;
            instrument.KiteInstrumentToken = kiteInstrument.InstrumentToken;

            upsertedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Synced {Count} Kite equity instruments from watchlist of {WatchlistCount}", upsertedCount, watchlist.Length);

        return upsertedCount;
    }
}
