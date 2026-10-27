namespace MarketsEngine.Domain.Entities;

/// <summary>
/// One OHLC candle for an instrument, imported from Kite Connect's historical-data API for use
/// by the backtest engine. "Interval" is a free string (e.g. "day") rather than an enum, matching
/// Kite's own interval values directly - future-proofing for intraday bars without a schema change.
/// </summary>
public class HistoricalBar
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Interval { get; set; } = "day";
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}
