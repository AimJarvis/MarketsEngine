using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Entities;

/// <summary>
/// A real-money order routed to Kite Connect. Deliberately not tied to a PaperAccount - there is
/// no simulated cash for real money, the real broker account is the source of truth for funds.
/// Kept in its own table, separate from PaperOrder, as an explicit, un-mixable audit trail.
/// </summary>
public class LiveOrder
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }
    public OrderSide Side { get; set; }
    public int Quantity { get; set; }
    public LiveOrderStatus Status { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>What triggered this order, e.g. "Strategy:SimpleMomentum", "Exit:TakeProfit", "Manual".</summary>
    public string Source { get; set; } = string.Empty;

    public string? RejectionReason { get; set; }

    /// <summary>Kite's own order_id once PlaceOrder has succeeded. Null until Submitted.</summary>
    public string? BrokerOrderId { get; set; }

    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>
    /// Carries the originating StrategySignal's stop-loss/take-profit override (if any) from
    /// submission through to the LivePosition the eventual fill opens - the live fill happens
    /// asynchronously (broker confirmation via poll or push), at which point the original
    /// SubmitLiveOrderRequest is long gone, so this is the only place left to read it from.
    /// </summary>
    public decimal? StopLossPercent { get; set; }
    public decimal? TakeProfitPercent { get; set; }

    /// <summary>
    /// One row per observed fill INCREMENT, not one row per order - Kite has no distinct
    /// "partially filled" status (a partial fill shows status Open with FilledQuantity &lt;
    /// Quantity), so an order that fills in pieces gets one LiveFill per piece, each recorded the
    /// moment LiveOrderReconciler observes FilledQuantity increase, regardless of whether the
    /// order ever reaches Complete.
    /// </summary>
    public List<LiveFill> Fills { get; set; } = new();
}
