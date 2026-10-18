namespace MarketsEngine.Domain.Enums;

/// <summary>
/// Milestone 1 only supports synchronous full-fill-or-reject execution, so there is
/// no Pending/PartiallyFilled/Cancelled state yet - those arrive with async/partial fills later.
/// </summary>
public enum OrderStatus
{
    Filled,
    Rejected
}
