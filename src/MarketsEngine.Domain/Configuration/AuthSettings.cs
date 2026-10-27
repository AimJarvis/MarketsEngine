using System.ComponentModel.DataAnnotations;

namespace MarketsEngine.Domain.Configuration;

/// <summary>
/// A single, self-issued API key required on every API request. This is a solo, local-first
/// app with no user/account system, so a static key (not OAuth/JWT) is the right fit - simple,
/// no login flow, no token expiry to manage. Required (not optional like Kite/Twilio) and
/// validated at startup, same bucket as TradingSettings - this app should never boot into a
/// state where real-money endpoints are reachable without a key configured.
/// </summary>
public class AuthSettings
{
    public const string SectionName = "Auth";

    [Required(AllowEmptyStrings = false)]
    [MinLength(16)]
    public string ApiKey { get; set; } = string.Empty;
}
