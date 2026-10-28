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

    /// <summary>
    /// Per-position override of Strategy:StopLossPercent/TakeProfitPercent, set from the
    /// StrategySignal that opened the position (null = strategy had no opinion, use the global
    /// default). Set only when the position is opened fresh; averaging into an existing position
    /// does not change it, so a later signal can't loosen/tighten the exit governing size already
    /// held.
    /// </summary>
    public decimal? StopLossPercent { get; set; }
    public decimal? TakeProfitPercent { get; set; }
}
