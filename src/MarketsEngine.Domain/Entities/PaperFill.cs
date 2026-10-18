namespace MarketsEngine.Domain.Entities;

public class PaperFill
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public PaperOrder? Order { get; set; }
    public int FilledQuantity { get; set; }
    public decimal FillPrice { get; set; }
    public decimal Fees { get; set; }
    public decimal SlippageAmount { get; set; }
    public DateTime FilledAtUtc { get; set; }
}
