namespace MarketsEngine.Domain.Entities;

/// <summary>
/// Persists the broker's login session (e.g. Kite's daily access token) across app restarts.
/// One row per provider - never log AccessToken/PublicToken, they're bearer credentials.
/// </summary>
public class BrokerSession
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? PublicToken { get; set; }
    public string? BrokerUserId { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}
