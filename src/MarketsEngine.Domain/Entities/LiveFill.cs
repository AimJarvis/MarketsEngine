namespace MarketsEngine.Domain.Entities;

/// <summary>Recorded once LiveOrderReconciliationService observes a real order reach COMPLETE on Kite.</summary>
public class LiveFill
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public LiveOrder? Order { get; set; }
    public int FilledQuantity { get; set; }
    public decimal FillPrice { get; set; }
    public DateTime FilledAtUtc { get; set; }
}
