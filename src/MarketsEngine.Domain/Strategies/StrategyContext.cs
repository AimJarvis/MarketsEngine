namespace MarketsEngine.Domain.Strategies;

/// <summary>
/// Everything a strategy needs to evaluate one instrument. RecentCandles is a short rolling window
/// of that instrument's own OHLCV bars, oldest first. Backtests fill it with real HistoricalBar
/// OHLCV; the live runner currently fills degenerate single-price candles (see Candle) - so a
/// close-only strategy behaves identically live and in backtest, while a model that uses full OHLCV
/// (Kronos) only gets real bars in backtest for now.
/// </summary>
public record StrategyContext(Guid InstrumentId, string TradingSymbol, IReadOnlyList<Candle> RecentCandles);
