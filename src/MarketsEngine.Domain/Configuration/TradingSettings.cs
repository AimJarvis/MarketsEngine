using System.ComponentModel.DataAnnotations;

namespace MarketsEngine.Domain.Configuration;

/// <summary>
/// Configurable paper-engine knobs - deliberately not hard-coded so fees/slippage/margin
/// never get silently presented as current real-market rates. Validated at startup via
/// ValidateOnStart() so a bad appsettings value fails fast instead of surfacing mid-request.
/// </summary>
public class TradingSettings
{
    public const string SectionName = "Trading";

    /// <summary>Global kill switch - when false, every order submission is rejected.</summary>
    public bool PaperTradingEnabled { get; set; } = true;

    [Range(0, 1000)]
    public int SlippageBasisPoints { get; set; } = 0;

    [Range(0, double.MaxValue)]
    public decimal FeePerLot { get; set; } = 20m;

    /// <summary>
    /// Flat margin blocked per net-short lot. This is a milestone-1 simplification, not real
    /// SPAN/exposure margin - replace before using this for anything beyond paper trading.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal ShortOptionMarginPerLot { get; set; } = 50000m;

    [Range(1, int.MaxValue)]
    public int QuoteStalenessSeconds { get; set; } = 60;
}
