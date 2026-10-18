using MarketsEngine.Domain.Trading;

namespace MarketsEngine.Tests;

public class MarginCalculatorTests
{
    [Fact]
    public void CalculateAdditionalMarginRequired_OpeningShortPosition_ReturnsMarginForNewShortLots()
    {
        const int currentNetQuantity = 0;
        const int newNetQuantity = -2;
        const decimal marginPerShortLot = 50_000m;

        var margin = MarginCalculator.CalculateAdditionalMarginRequired(currentNetQuantity, newNetQuantity, marginPerShortLot);

        Assert.Equal(100_000m, margin);
    }

    [Fact]
    public void CalculateAdditionalMarginRequired_IncreasingLongPosition_ReturnsZero()
    {
        const int currentNetQuantity = 1;
        const int newNetQuantity = 3;
        const decimal marginPerShortLot = 50_000m;

        var margin = MarginCalculator.CalculateAdditionalMarginRequired(currentNetQuantity, newNetQuantity, marginPerShortLot);

        Assert.Equal(0m, margin);
    }

    [Fact]
    public void CalculateMarginRelease_ReducingShortPosition_ReturnsMarginForClosedLots()
    {
        const int currentNetQuantity = -3;
        const int newNetQuantity = -1;
        const decimal marginPerShortLot = 50_000m;

        var release = MarginCalculator.CalculateMarginRelease(currentNetQuantity, newNetQuantity, marginPerShortLot);

        Assert.Equal(100_000m, release);
    }

    [Fact]
    public void CalculateMarginRelease_FullyClosingShortPosition_ReturnsFullMargin()
    {
        const int currentNetQuantity = -2;
        const int newNetQuantity = 0;
        const decimal marginPerShortLot = 50_000m;

        var release = MarginCalculator.CalculateMarginRelease(currentNetQuantity, newNetQuantity, marginPerShortLot);

        Assert.Equal(100_000m, release);
    }
}
