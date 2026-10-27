using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using MarketsEngine.Domain.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Api.Auth;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions;

/// <summary>
/// Checks a single static key, sent as the X-Api-Key header, against Auth:ApiKey. Uses a
/// fixed-time comparison so response timing can't leak how much of the key matched - this
/// guards endpoints that can place real-money orders.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthSettings> authSettingsOptions)
    : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var providedKey) || string.IsNullOrEmpty(providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Missing {HeaderName} header."));
        }

        var configuredKey = authSettingsOptions.Value.ApiKey;
        var providedBytes = Encoding.UTF8.GetBytes(providedKey.ToString());
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);

        if (!CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
