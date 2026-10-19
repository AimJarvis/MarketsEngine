using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Notifications;
using MarketsEngine.Infrastructure.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController(
    IWhatsAppNotifier notifier, PnLAlertService alertService, IOptionsMonitor<PnLAlertSettings> settingsMonitor) : ControllerBase
{
    [HttpPost("test")]
    public async Task<IActionResult> SendTestMessage(CancellationToken cancellationToken)
    {
        try
        {
            await notifier.SendAsync("MarketsEngine: this is a test WhatsApp alert. If you got this, Twilio is wired up correctly.", cancellationToken);
            return Ok(new { message = "Test message sent - check your WhatsApp." });
        }
        catch (TwilioNotConfiguredException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("pnl-check-once")]
    public async Task<IActionResult> RunPnLCheckOnce(CancellationToken cancellationToken)
    {
        await alertService.CheckOnceAsync(settingsMonitor.CurrentValue, cancellationToken);
        return Ok(new { message = "P&L check completed - an alert was sent only if the threshold was crossed." });
    }
}
