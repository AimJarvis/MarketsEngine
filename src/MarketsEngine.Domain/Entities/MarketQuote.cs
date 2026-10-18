namespace MarketsEngine.Domain.Entities;

public class MarketQuote
{
    public Guid Id { get; set; }
    public Guid InstrumentId { get; set; }
    public Instrument? Instrument { get; set; }
    public decimal BidPrice { get; set; }
    public decimal AskPrice { get; set; }
    public decimal LastTradedPrice { get; set; }
    public string DataSource { get; set; } = "Mock";
    public DateTime QuotedAtUtc { get; set; }
}
