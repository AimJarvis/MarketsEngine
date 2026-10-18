using KiteConnect;
using MarketsEngine.Domain.Configuration;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Infrastructure.Brokers;

/// <summary>
/// Builds Kite clients on demand - construction is cheap, so no pooling/caching is needed.
/// Throws KiteNotConfiguredException with a clear message instead of a generic null-ref
/// when ApiKey/ApiSecret or a stored session are missing, since Kite is optional.
/// </summary>
public class KiteClientFactory(IOptions<KiteConnectSettings> settingsOptions, KiteSessionStore sessionStore)
{
    private readonly KiteConnectSettings _settings = settingsOptions.Value;

    public string ApiKey =>
        _settings.ApiKey ?? throw new KiteNotConfiguredException(
            "Kite:ApiKey is not configured. Set it with: dotnet user-secrets set \"Kite:ApiKey\" \"...\"");

    public string ApiSecret =>
        _settings.ApiSecret ?? throw new KiteNotConfiguredException(
            "Kite:ApiSecret is not configured. Set it with: dotnet user-secrets set \"Kite:ApiSecret\" \"...\"");

    public Kite CreateUnauthenticatedClient() => new(ApiKey);

    public async Task<Kite> CreateAuthenticatedClientAsync(CancellationToken cancellationToken = default)
    {
        var session = await sessionStore.GetAsync(cancellationToken)
            ?? throw new KiteNotConfiguredException("No Kite session found. Complete the login flow first: GET /api/broker/kite/login-url");

        return new Kite(ApiKey, session.AccessToken);
    }
}

public class KiteNotConfiguredException(string message) : Exception(message);
