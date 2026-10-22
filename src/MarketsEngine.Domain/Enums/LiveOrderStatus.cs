namespace MarketsEngine.Domain.Enums;

/// <summary>
/// Models Kite's real, asynchronous order lifecycle - unlike the paper engine's
/// Filled/Rejected-only OrderStatus, a real order is accepted before it's known to have filled.
/// </summary>
public enum LiveOrderStatus
{
    PendingApproval,
    Rejected,
    Approved,
    Submitted,
    Open,
    PartiallyFilled,
    Complete,
    Cancelled,
    Failed
}
