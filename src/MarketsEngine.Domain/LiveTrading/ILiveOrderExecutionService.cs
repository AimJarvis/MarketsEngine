using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.LiveTrading;

public interface ILiveOrderExecutionService
{
    /// <summary>Validates and (if RequireManualApproval) parks a real-money order as PendingApproval. Never calls Kite directly.</summary>
    Task<LiveOrderSubmissionResult> SubmitOrderAsync(SubmitLiveOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Re-validates risk/kill-switch and places the real order with Kite.</summary>
    Task<LiveOrderSubmissionResult> ApproveAsync(Guid liveOrderId, CancellationToken cancellationToken = default);

    /// <summary>Marks a pending order Rejected without ever contacting Kite.</summary>
    Task<LiveOrderSubmissionResult> RejectAsync(Guid liveOrderId, string reason, CancellationToken cancellationToken = default);
}

public record SubmitLiveOrderRequest(Guid InstrumentId, OrderSide Side, int Quantity, string IdempotencyKey, string Source);

public record LiveOrderSubmissionResult(bool Success, bool PendingApproval, string? ErrorCode, string? ErrorMessage, LiveOrder? Order)
{
    public static LiveOrderSubmissionResult Rejected(string errorCode, string errorMessage) =>
        new(false, false, errorCode, errorMessage, null);

    public static LiveOrderSubmissionResult Pending(LiveOrder order) => new(true, true, null, null, order);

    public static LiveOrderSubmissionResult Submitted(LiveOrder order) => new(true, false, null, null, order);
}
