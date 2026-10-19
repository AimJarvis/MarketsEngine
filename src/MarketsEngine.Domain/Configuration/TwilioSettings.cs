namespace MarketsEngine.Domain.Configuration;

/// <summary>
/// Optional - the app must keep working without these set. Set via user-secrets, never
/// appsettings.json: dotnet user-secrets set "Twilio:AccountSid" "..." (and AuthToken).
/// Both numbers must be in "whatsapp:+&lt;E.164 number&gt;" format, e.g. "whatsapp:+14155238886"
/// for Twilio's sandbox number. To use the free sandbox, send "join &lt;your-keyword&gt;" from
/// your WhatsApp to that number once before any message will be delivered to you.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? WhatsAppFromNumber { get; set; }
    public string? WhatsAppToNumber { get; set; }

    /// <summary>
    /// A pre-approved WhatsApp Content Template id (starts "HX..."), required for any
    /// business-initiated message (which all of ours are - we message on our own schedule, not
    /// as a reply) outside an active 24h session window. Must have exactly one variable, which
    /// receives the full alert/test message text. Find one under Console → Messaging →
    /// Content Template Builder (the sandbox pre-provisions some already-approved ones).
    /// If unset, falls back to a free-form body send, which only works inside a session window.
    /// </summary>
    public string? ContentSid { get; set; }
}
