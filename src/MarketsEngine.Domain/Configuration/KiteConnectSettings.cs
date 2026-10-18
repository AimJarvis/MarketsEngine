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
}
