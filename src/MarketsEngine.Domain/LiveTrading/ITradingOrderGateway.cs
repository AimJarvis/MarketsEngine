using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.LiveTrading;

/// <summary>
/// The single entry point every order-submitting caller (StrategyRunnerService, controllers)
/// depends on. Callers never reference IOrderExecutionService or ILiveOrderExecutionService
/// directly - the implementation reads LiveTrading:Enabled and routes accordingly, so switching
/// the whole system between paper and real money is a config change, not a code change.
/// </summary>
public interface ITradingOrderGateway
{
    Task<GatewayOrderResult> SubmitAsync(GatewayOrderRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// AccountId is only meaningful in paper mode (the simulated cash account to post against) and is
/// ignored when routed live, where the real broker account is the only account.
/// </summary>
public record GatewayOrderRequest(
    Guid? AccountId,
    Guid InstrumentId,
    OrderSide Side,
    TradingStyle TradingStyle,
    int Quantity,
    string IdempotencyKey,
    string Source);

public record GatewayOrderResult(
    bool Success,
    bool PendingApproval,
    string Mode,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? OrderId)
{
    public static GatewayOrderResult Rejected(string mode, string errorCode, string errorMessage) =>
        new(false, false, mode, errorCode, errorMessage, null);

    public static GatewayOrderResult Filled(string mode, Guid orderId) =>
        new(true, false, mode, null, null, orderId);

    public static GatewayOrderResult Pending(string mode, Guid orderId) =>
        new(true, true, mode, null, null, orderId);
}
