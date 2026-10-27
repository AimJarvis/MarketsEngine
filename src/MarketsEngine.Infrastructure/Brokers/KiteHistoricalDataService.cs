using KiteConnect;
using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Imports daily OHLC candles from Kite Connect's historical-data API into HistoricalBar, for the
/// backtest engine to replay later. Mirrors KiteInstrumentSyncService's pattern exactly: read-only
/// market data, upserts by (InstrumentId, TimestampUtc, Interval), never touches order placement.
/// Requires the instrument to already have a KiteInstrumentToken (sync it first via
/// sync-instruments/sync-equities) and a completed Kite login - the same real-credentials
/// requirement as every other Kite-backed feature in this app.
/// </summary>
public class KiteHistoricalDataService(
    KiteClientFactory clientFactory,
    MarketsEngineDbContext dbContext,
    ILogger<KiteHistoricalDataService> logger)
{
    private const string DailyInterval = "day";

    public async Task<int> ImportHistoricalDataAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var instrument = await dbContext.Instruments.FirstOrDefaultAsync(i => i.Id == instrumentId, cancellationToken)
            ?? throw new InvalidOperationException("Instrument not found.");

        if (instrument.KiteInstrumentToken is null)
        {
            throw new InvalidOperationException(
                "This instrument has no KiteInstrumentToken - sync it from Kite first: " +
                "POST /api/broker/kite/sync-instruments or /sync-equities.");
        }

        var kite = await clientFactory.CreateAuthenticatedClientAsync(cancellationToken);

        var candles = kite.GetHistoricalData(
            InstrumentToken: instrument.KiteInstrumentToken.Value.ToString(),
            FromDate: from.ToDateTime(TimeOnly.MinValue),
            ToDate: to.ToDateTime(TimeOnly.MinValue),
            Interval: Constants.Interval.Day);

        var existingTimestamps = await dbContext.HistoricalBars
            .Where(b => b.InstrumentId == instrumentId && b.Interval == DailyInterval)
            .Select(b => b.TimestampUtc)
            .ToListAsync(cancellationToken);

        var existingSet = existingTimestamps.ToHashSet();
        var upsertedCount = 0;

        foreach (var candle in candles)
        {
            var timestampUtc = DateTime.SpecifyKind(candle.TimeStamp, DateTimeKind.Utc);

            if (existingSet.Contains(timestampUtc))
            {
                continue;
            }

            dbContext.HistoricalBars.Add(new HistoricalBar
            {
                Id = Guid.NewGuid(),
                InstrumentId = instrumentId,
                TimestampUtc = timestampUtc,
                Interval = DailyInterval,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = (long)candle.Volume
            });

            upsertedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Imported {Count} new daily bars for {Symbol} ({From} to {To})",
            upsertedCount, instrument.TradingSymbol, from, to);

        return upsertedCount;
    }
}
