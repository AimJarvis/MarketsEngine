namespace MarketsEngine.Domain.Entities;

/// <summary>
/// Real-money equivalent of PaperPosition, maintained purely from our own recorded LiveFills via
/// the same PositionUpdater.ApplyFill used by the paper engine - this is NOT reconciled against
/// Kite's actual broker-side holdings/positions API, the same simplification PaperPosition
/// already makes relative to real margin accounting.
/// </summary>
public class LivePosition
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }

    /// <summary>Positive = net long (bought), negative = net short (sold), in lots.</summary>
    public int NetQuantity { get; set; }
    public decimal AverageEntryPrice { get; set; }
    public decimal RealizedPnL { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }
}
