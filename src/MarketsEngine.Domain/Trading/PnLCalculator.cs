namespace MarketsEngine.Domain.Trading;

public static class PnLCalculator
{
    public static decimal CalculateUnrealizedPnL(int netQuantity, decimal averageEntryPrice, decimal currentPrice, int lotSize)
    {
        if (netQuantity == 0)
        {
            return 0m;
        }

        var units = Math.Abs(netQuantity) * lotSize;
        return netQuantity > 0
            ? (currentPrice - averageEntryPrice) * units
            : (averageEntryPrice - currentPrice) * units;
    }
}
