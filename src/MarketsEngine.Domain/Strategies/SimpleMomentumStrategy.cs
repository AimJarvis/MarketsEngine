namespace MarketsEngine.Domain.Strategies;

/// <summary>
/// V1 baseline strategy: if an instrument's own last-traded-price has risen by at least
/// ThresholdPercent between the oldest and newest sample in the rolling window, buy more of it.
///
/// This is a deliberate simplification, not a real trading edge: it measures momentum on the
/// option's own premium (which drifts with IV/theta, not just direction) rather than the
/// underlying index, and it never considers shorting. It exists to prove the strategy →
/// signal → automated order pipeline end to end; a real strategy needs real backtesting
/// against historical data before it should be trusted with anything, paper or otherwise.
/// </summary>
public class SimpleMomentumStrategy(decimal thresholdPercent) : IStrategy
{
    public Task<StrategySignal> EvaluateAsync(StrategyContext context, CancellationToken cancellationToken = default)
    {
        if (context.RecentCandles.Count < 2)
        {
            return Task.FromResult(StrategySignal.None);
        }

        // Close-only: this strategy's momentum signal doesn't use intrabar range or volume, so it
        // behaves identically on real backtest candles and on the live path's degenerate candles.
        var oldest = context.RecentCandles[0].Close;
        var newest = context.RecentCandles[^1].Close;

        if (oldest <= 0)
        {
            return Task.FromResult(StrategySignal.None);
        }

        var percentChange = (newest - oldest) / oldest * 100m;

        return Task.FromResult(percentChange >= thresholdPercent
            ? new StrategySignal(true, $"{context.TradingSymbol} up {percentChange:F2}% over window (threshold {thresholdPercent:F2}%)")
            : StrategySignal.None);
    }
}
