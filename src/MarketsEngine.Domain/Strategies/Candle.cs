namespace MarketsEngine.Domain.Strategies;

/// <summary>
/// One OHLCV bar of market data, the unit a strategy reasons over. Replaces the old price-only
/// PricePoint so forecasting models (Kronos) get the open/high/low/close/volume structure they
/// were actually trained on, instead of a single collapsed price.
///
/// Backtests populate this from real HistoricalBar OHLCV. The live strategy runner currently only
/// has point-in-time quote snapshots (not real intraday bars), so it fills a DEGENERATE candle
/// (Open=High=Low=Close=last-traded-price, Volume=0) - correct for close-only strategies like
/// SimpleMomentum, but it does NOT give Kronos real intrabar range/volume live. Real-time OHLCV
/// bar aggregation from the tick stream is the remaining piece before live Kronos is meaningful
/// (see docs/TODO.md); until then, trust Kronos only from backtests, which do feed it real bars.
/// </summary>
public readonly record struct Candle(
    DateTime TimestampUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume)
{
    /// <summary>A degenerate single-price candle (no real OHLC/volume), for the live quote-snapshot path.</summary>
    public static Candle FromPrice(DateTime timestampUtc, decimal price) =>
        new(timestampUtc, price, price, price, price, 0m);
}
