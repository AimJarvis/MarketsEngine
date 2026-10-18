using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Trading;

namespace MarketsEngine.Tests;

public class FillPriceCalculatorTests
{
    [Fact]
    public void CalculateFillPrice_BuyWithZeroSlippage_ReturnsAskPrice()
    {
        const decimal bidPrice = 100m;
        const decimal askPrice = 105m;

        var fillPrice = FillPriceCalculator.CalculateFillPrice(OrderSide.Buy, bidPrice, askPrice, slippageBasisPoints: 0);

        Assert.Equal(askPrice, fillPrice);
    }

    [Fact]
    public void CalculateFillPrice_SellWithZeroSlippage_ReturnsBidPrice()
    {
        const decimal bidPrice = 100m;
        const decimal askPrice = 105m;

        var fillPrice = FillPriceCalculator.CalculateFillPrice(OrderSide.Sell, bidPrice, askPrice, slippageBasisPoints: 0);

        Assert.Equal(bidPrice, fillPrice);
    }

    [Fact]
    public void CalculateFillPrice_BuyWithSlippage_FillsAboveAskPrice()
    {
        const decimal bidPrice = 100m;
        const decimal askPrice = 100m;
        const int slippageBasisPoints = 50;

        var fillPrice = FillPriceCalculator.CalculateFillPrice(OrderSide.Buy, bidPrice, askPrice, slippageBasisPoints);

        Assert.Equal(100.5m, fillPrice);
    }

    [Fact]
    public void CalculateFillPrice_SellWithSlippage_FillsBelowBidPrice()
    {
        const decimal bidPrice = 100m;
        const decimal askPrice = 100m;
        const int slippageBasisPoints = 50;

        var fillPrice = FillPriceCalculator.CalculateFillPrice(OrderSide.Sell, bidPrice, askPrice, slippageBasisPoints);

        Assert.Equal(99.5m, fillPrice);
    }
}
