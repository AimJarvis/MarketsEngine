using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Trading;

namespace MarketsEngine.Tests;

public class PositionUpdaterTests
{
    [Fact]
    public void ApplyFill_OpeningLongPositionFromFlat_SetsNetQuantityAndAveragePrice()
    {
        const int currentNetQuantity = 0;
        const decimal currentAverageEntryPrice = 0m;
        const int filledQuantity = 2;
        const decimal fillPrice = 180m;
        const int lotSize = 75;

        var result = PositionUpdater.ApplyFill(currentNetQuantity, currentAverageEntryPrice, OrderSide.Buy, filledQuantity, fillPrice, lotSize);

        Assert.Equal(new PositionUpdater.Result(2, 180m, 0m), result);
    }

    [Fact]
    public void ApplyFill_IncreasingExistingLongPosition_ReweightsAverageEntryPrice()
    {
        const int currentNetQuantity = 1;
        const decimal currentAverageEntryPrice = 100m;
        const int filledQuantity = 1;
        const decimal fillPrice = 200m;
        const int lotSize = 10;

        var result = PositionUpdater.ApplyFill(currentNetQuantity, currentAverageEntryPrice, OrderSide.Buy, filledQuantity, fillPrice, lotSize);

        Assert.Equal(new PositionUpdater.Result(2, 150m, 0m), result);
    }

    [Fact]
    public void ApplyFill_PartiallyClosingLongPosition_RealizesProportionalPnLAndKeepsAveragePrice()
    {
        const int currentNetQuantity = 2;
        const decimal currentAverageEntryPrice = 100m;
        const int filledQuantity = 1;
        const decimal fillPrice = 120m;
        const int lotSize = 10;

        var result = PositionUpdater.ApplyFill(currentNetQuantity, currentAverageEntryPrice, OrderSide.Sell, filledQuantity, fillPrice, lotSize);

        Assert.Equal(new PositionUpdater.Result(1, 100m, 200m), result);
    }

    [Fact]
    public void ApplyFill_FullyClosingLongPosition_ResetsAverageEntryPriceToZero()
    {
        const int currentNetQuantity = 1;
        const decimal currentAverageEntryPrice = 100m;
        const int filledQuantity = 1;
        const decimal fillPrice = 130m;
        const int lotSize = 10;

        var result = PositionUpdater.ApplyFill(currentNetQuantity, currentAverageEntryPrice, OrderSide.Sell, filledQuantity, fillPrice, lotSize);

        Assert.Equal(new PositionUpdater.Result(0, 0m, 300m), result);
    }

    [Fact]
    public void ApplyFill_SellQuantityExceedingLongPosition_FlipsToShortAtFillPrice()
    {
        const int currentNetQuantity = 1;
        const decimal currentAverageEntryPrice = 100m;
        const int filledQuantity = 3;
        const decimal fillPrice = 110m;
        const int lotSize = 10;

        var result = PositionUpdater.ApplyFill(currentNetQuantity, currentAverageEntryPrice, OrderSide.Sell, filledQuantity, fillPrice, lotSize);

        Assert.Equal(new PositionUpdater.Result(-2, 110m, 100m), result);
    }

    [Fact]
    public void ApplyFill_OpeningShortPositionFromFlat_SetsNegativeNetQuantity()
    {
        const int currentNetQuantity = 0;
        const decimal currentAverageEntryPrice = 0m;
        const int filledQuantity = 1;
        const decimal fillPrice = 50m;
        const int lotSize = 30;

        var result = PositionUpdater.ApplyFill(currentNetQuantity, currentAverageEntryPrice, OrderSide.Sell, filledQuantity, fillPrice, lotSize);

        Assert.Equal(new PositionUpdater.Result(-1, 50m, 0m), result);
    }
}
