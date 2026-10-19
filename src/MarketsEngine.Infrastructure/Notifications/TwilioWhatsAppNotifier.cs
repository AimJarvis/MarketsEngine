using System.Text.Json;
using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Notifications;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace MarketsEngine.Infrastructure.Notifications;

public class TwilioWhatsAppNotifier(IOptions<TwilioSettings> settingsOptions) : IWhatsAppNotifier
{
    private readonly TwilioSettings _settings = settingsOptions.Value;

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        var accountSid = _settings.AccountSid
            ?? throw new TwilioNotConfiguredException("Twilio:AccountSid is not configured.");
        var authToken = _settings.AuthToken
            ?? throw new TwilioNotConfiguredException("Twilio:AuthToken is not configured.");
        var fromNumber = _settings.WhatsAppFromNumber
            ?? throw new TwilioNotConfiguredException("Twilio:WhatsAppFromNumber is not configured.");
        var toNumber = _settings.WhatsAppToNumber
            ?? throw new TwilioNotConfiguredException("Twilio:WhatsAppToNumber is not configured.");

        TwilioClient.Init(accountSid, authToken);

        if (!string.IsNullOrWhiteSpace(_settings.ContentSid))
        {
            // Business-initiated message (our alerts aren't replies to anything) - WhatsApp
            // requires a pre-approved template outside an active 24h session window.
            var contentVariables = JsonSerializer.Serialize(new Dictionary<string, string> { ["1"] = message });

            await MessageResource.CreateAsync(
                from: new PhoneNumber(fromNumber),
                to: new PhoneNumber(toNumber),
                contentSid: _settings.ContentSid,
                contentVariables: contentVariables);
        }
        else
        {
            // Only works inside an active session window (i.e. you messaged the sandbox
            // number recently). Set Twilio:ContentSid to send reliably outside that window.
            await MessageResource.CreateAsync(
                from: new PhoneNumber(fromNumber),
                to: new PhoneNumber(toNumber),
                body: message);
        }
    }
}

public class TwilioNotConfiguredException(string message) : Exception(
    message + " Set it with: dotnet user-secrets set \"Twilio:...\" \"...\" in MarketsEngine.Api.");
