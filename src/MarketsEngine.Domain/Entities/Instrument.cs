using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Entities;

public class Instrument
{
    public Guid Id { get; set; }
    public string TradingSymbol { get; set; } = string.Empty;
    public Underlying Underlying { get; set; }
    public OptionType OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int LotSize { get; set; }
    public string DataSource { get; set; } = "Mock";
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Kite's instrument_token, used to subscribe this instrument on the ticker. Null for mock instruments.</summary>
    public long? KiteInstrumentToken { get; set; }
}
