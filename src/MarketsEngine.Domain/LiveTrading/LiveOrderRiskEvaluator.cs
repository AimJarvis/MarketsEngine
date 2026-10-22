using MarketsEngine.Domain.Configuration;

namespace MarketsEngine.Domain.LiveTrading;

/// <summary>
/// Pure, config-driven real-money risk gate - checked once when a signal is parked as
/// PendingApproval and again right before the real Kite PlaceOrder call, since config or
/// the day's traded notional may have changed in between.
/// </summary>
public static class LiveOrderRiskEvaluator
{
    public enum RiskResult { Approved, Rejected }

    public record Outcome(RiskResult Result, string? ErrorCode, string? ErrorMessage)
    {
        public static readonly Outcome Ok = new(RiskResult.Approved, null, null);
        public static Outcome Reject(string errorCode, string errorMessage) => new(RiskResult.Rejected, errorCode, errorMessage);
    }

    public static Outcome Evaluate(
        int quantity,
        decimal estimatedPrice,
        int lotSize,
        decimal notionalAlreadyTradedToday,
        LiveTradingSettings settings)
    {
        if (quantity > settings.MaxQuantityPerOrder)
        {
            return Outcome.Reject(
                "MaxQuantityExceeded",
                $"Order quantity {quantity} exceeds LiveTrading:MaxQuantityPerOrder ({settings.MaxQuantityPerOrder}).");
        }

        var orderNotional = estimatedPrice * lotSize * quantity;

        if (orderNotional > settings.MaxNotionalPerOrder)
        {
            return Outcome.Reject(
                "MaxNotionalPerOrderExceeded",
                $"Order notional {orderNotional:F2} exceeds LiveTrading:MaxNotionalPerOrder ({settings.MaxNotionalPerOrder}).");
        }

        if (notionalAlreadyTradedToday + orderNotional > settings.MaxNotionalPerDay)
        {
            return Outcome.Reject(
                "MaxNotionalPerDayExceeded",
                $"Today's traded notional {notionalAlreadyTradedToday:F2} plus this order's {orderNotional:F2} " +
                $"would exceed LiveTrading:MaxNotionalPerDay ({settings.MaxNotionalPerDay}).");
        }

        return Outcome.Ok;
    }
}
