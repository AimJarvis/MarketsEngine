using MarketsEngine.Api.Contracts;
using MarketsEngine.Domain.Configuration;
using MarketsEngine.Infrastructure.Strategies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Api.Controllers;

/// <summary>
/// Visibility and manual control for the automated strategy runner. The actual on/off switch
/// and strategy choice live in appsettings.json ("Strategy" section) - this controller lets you
/// check what's configured and trigger one evaluation pass immediately instead of waiting for
/// the background timer, mainly useful for testing.
/// </summary>
[ApiController]
[Route("api/strategy")]
public class StrategyController(IOptionsMonitor<StrategySettings> settingsMonitor, StrategyRunnerService runner) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<StrategyStatusResponse> GetStatus()
    {
        var settings = settingsMonitor.CurrentValue;
        return Ok(new StrategyStatusResponse(
            settings.Enabled, settings.ActiveStrategy, settings.InstrumentKindFilter,
            settings.AccountId, settings.EvaluationIntervalSeconds));
    }

    [HttpPost("run-once")]
    public async Task<IActionResult> RunOnce(CancellationToken cancellationToken)
    {
        var settings = settingsMonitor.CurrentValue;

        if (settings.AccountId is null)
        {
            return BadRequest(new { message = "Strategy:AccountId is not configured. Set it in appsettings.json to a real paper account id." });
        }

        await runner.RunOnceAsync(settings, cancellationToken);
        return Ok(new { message = "Strategy evaluation pass completed - check logs/order history for results." });
    }
}
