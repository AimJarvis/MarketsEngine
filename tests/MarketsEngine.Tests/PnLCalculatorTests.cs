using MarketsEngine.Domain.Trading;

namespace MarketsEngine.Tests;

public class PnLCalculatorTests
{
    [Fact]
    public void CalculateUnrealizedPnL_FlatPosition_ReturnsZero()
    {
        var pnl = PnLCalculator.CalculateUnrealizedPnL(netQuantity: 0, averageEntryPrice: 100m, currentPrice: 150m, lotSize: 10);

        Assert.Equal(0m, pnl);
    }

    [Fact]
    public void CalculateUnrealizedPnL_LongPositionPriceUp_ReturnsPositivePnL()
    {
        const int netQuantity = 2;
        const decimal averageEntryPrice = 100m;
        const decimal currentPrice = 120m;
        const int lotSize = 10;

        var pnl = PnLCalculator.CalculateUnrealizedPnL(netQuantity, averageEntryPrice, currentPrice, lotSize);

        Assert.Equal(400m, pnl);
    }

    [Fact]
    public void CalculateUnrealizedPnL_ShortPositionPriceDown_ReturnsPositivePnL()
    {
        const int netQuantity = -2;
        const decimal averageEntryPrice = 100m;
        const decimal currentPrice = 80m;
        const int lotSize = 10;

        var pnl = PnLCalculator.CalculateUnrealizedPnL(netQuantity, averageEntryPrice, currentPrice, lotSize);

        Assert.Equal(400m, pnl);
    }

    [Fact]
    public void CalculateUnrealizedPnL_ShortPositionPriceUp_ReturnsNegativePnL()
    {
        const int netQuantity = -1;
        const decimal averageEntryPrice = 100m;
        const decimal currentPrice = 130m;
        const int lotSize = 10;

        var pnl = PnLCalculator.CalculateUnrealizedPnL(netQuantity, averageEntryPrice, currentPrice, lotSize);

        Assert.Equal(-300m, pnl);
    }
}
