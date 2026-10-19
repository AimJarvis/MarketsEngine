namespace MarketsEngine.Domain.Configuration;

/// <summary>
/// Optional - the app must keep working in pure mock mode without these set. Only an
/// actual Kite endpoint call fails (clearly) if they're missing. Set via user-secrets,
/// never appsettings.json: dotnet user-secrets set "Kite:ApiKey" "..." (and ApiSecret).
/// </summary>
public class KiteConnectSettings
{
    public const string SectionName = "Kite";

    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }

    /// <summary>
    /// NSE trading symbols to sync/trade as equities, e.g. ["RELIANCE", "TCS", "INFY"].
    /// Deliberately not a fixed list in code - NSE has thousands of listed equities, so which
    /// ones matter is a config choice, not a constant. Edit this in appsettings.json.
    /// </summary>
    public string[] EquityWatchlist { get; set; } = ["RELIANCE", "TCS", "INFY", "HDFCBANK"];
}
