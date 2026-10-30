namespace MarketsEngine.Domain.Entities;

/// <summary>
/// One fill INCREMENT for a LiveOrder - recorded every time LiveOrderReconciler observes Kite's
/// cumulative FilledQuantity for the order increase, whether that's the whole order filling at
/// once or one piece of a partial fill. A single LiveOrder can have many of these.
/// </summary>
public class LiveFill
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public LiveOrder? Order { get; set; }

    /// <summary>How many units THIS increment added (actual units, not lots) - not cumulative.</summary>
    public int FilledQuantity { get; set; }
    public decimal FillPrice { get; set; }
    public DateTime FilledAtUtc { get; set; }

    /// <summary>
    /// Kite's own cumulative FilledQuantity for the order at the moment this increment was
    /// observed. Exists purely so (OrderId, CumulativeFilledQuantityAfter) can be a unique DB
    /// index: the poll path (LiveOrderReconciliationService) and the push path
    /// (KiteTickerService.OnOrderUpdate) can both observe the same real-world increment and race
    /// to record it, and the DB - not in-memory logic - is what arbitrates that race, the same
    /// idempotency idiom already used for PaperOrder/LiveOrder's own IdempotencyKey.
    /// </summary>
    public int CumulativeFilledQuantityAfter { get; set; }
}
