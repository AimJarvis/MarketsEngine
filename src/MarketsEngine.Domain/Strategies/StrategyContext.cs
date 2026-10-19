namespace MarketsEngine.Domain.Strategies;

/// <summary>
/// Everything a strategy needs to evaluate one instrument. RecentPrices is a short rolling
/// window of that instrument's own last-traded-price samples, oldest first - kept in memory by
/// the strategy runner, independent of how MarketQuotes itself stores/refreshes quotes.
/// </summary>
public record StrategyContext(Guid InstrumentId, string TradingSymbol, IReadOnlyList<PricePoint> RecentPrices);
