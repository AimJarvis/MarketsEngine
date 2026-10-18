namespace MarketsEngine.Domain.Entities;

public class PaperPosition
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public PaperAccount? Account { get; set; }
    public Guid InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }

    /// <summary>Positive = net long (bought), negative = net short (sold), in lots.</summary>
    public int NetQuantity { get; set; }
    public decimal AverageEntryPrice { get; set; }
    public decimal RealizedPnL { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }
}
