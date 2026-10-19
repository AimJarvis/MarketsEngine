using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Entities;

/// <summary>
/// Represents either an option contract or a plain equity, distinguished by Kind.
/// For Kind=Equity: Underlying/OptionType/ExpiryDate are null (no index, no call/put, never
/// expires) and StrikePrice is 0 (not meaningful); LotSize is 1 (equities trade per-share, so
/// Quantity on an order already means "number of shares" with no extra multiplier).
/// </summary>
public class Instrument
{
    public Guid Id { get; set; }
    public string TradingSymbol { get; set; } = string.Empty;
    public InstrumentKind Kind { get; set; }
    public Underlying? Underlying { get; set; }
    public OptionType? OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public int LotSize { get; set; }
    public string DataSource { get; set; } = "Mock";
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Kite's instrument_token, used to subscribe this instrument on the ticker. Null for mock instruments.</summary>
    public long? KiteInstrumentToken { get; set; }
}
