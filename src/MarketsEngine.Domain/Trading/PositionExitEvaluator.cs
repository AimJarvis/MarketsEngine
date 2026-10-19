namespace MarketsEngine.Domain.Trading;

/// <summary>
/// Fixed take-profit / stop-loss exit rule, evaluated against an existing position's unrealized
/// return (not just raw price move), so it works correctly for both long and short positions.
/// This is a simplification, not a real risk engine: no trailing stops, no partial closes, no
/// time-based exits - it closes the whole position the instant either threshold is crossed.
/// </summary>
public static class PositionExitEvaluator
{
    public enum ExitReason
    {
        None,
        TakeProfit,
        StopLoss
    }

    public static ExitReason Evaluate(
        int netQuantity, decimal averageEntryPrice, decimal currentPrice, int lotSize,
        decimal takeProfitPercent, decimal stopLossPercent)
    {
        if (netQuantity == 0 || averageEntryPrice <= 0)
        {
            return ExitReason.None;
        }

        var costBasis = averageEntryPrice * lotSize * Math.Abs(netQuantity);
        if (costBasis <= 0)
        {
            return ExitReason.None;
        }

        var unrealizedPnL = PnLCalculator.CalculateUnrealizedPnL(netQuantity, averageEntryPrice, currentPrice, lotSize);
        var percentReturn = unrealizedPnL / costBasis * 100m;

        if (percentReturn >= takeProfitPercent)
        {
            return ExitReason.TakeProfit;
        }

        if (percentReturn <= -stopLossPercent)
        {
            return ExitReason.StopLoss;
        }

        return ExitReason.None;
    }
}
