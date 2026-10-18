using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Trading;

public static class FillPriceCalculator
{
    /// <summary>
    /// Simulated full-fill price: a buy lifts the ask, a sell hits the bid, with configurable
    /// slippage applied against the trader (buys fill higher, sells fill lower).
    /// </summary>
    public static decimal CalculateFillPrice(OrderSide side, decimal bidPrice, decimal askPrice, int slippageBasisPoints)
    {
        var basePrice = side == OrderSide.Buy ? askPrice : bidPrice;
        var slippage = basePrice * slippageBasisPoints / 10_000m;
        return side == OrderSide.Buy ? basePrice + slippage : basePrice - slippage;
    }
}
