using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Entities;

public class PaperOrder
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public PaperAccount? Account { get; set; }
    public Guid InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }
    public OrderSide Side { get; set; }
    public TradingStyle TradingStyle { get; set; }
    public int Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }

    public PaperFill? Fill { get; set; }
}
