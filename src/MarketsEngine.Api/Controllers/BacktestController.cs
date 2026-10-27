using MarketsEngine.Api.Contracts;
using MarketsEngine.Domain.Backtesting;
using MarketsEngine.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketsEngine.Api.Controllers;

/// <summary>
/// Replays imported historical bars (see BrokerController's import-historical endpoint) through a
/// real strategy to see whether it would have made money - entirely separate from paper/live
/// trading, never places any order anywhere.
/// </summary>
[ApiController]
[Route("api/backtests")]
public class BacktestController(BacktestRunnerService runner) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run([FromBody] BacktestApiRequest request, CancellationToken cancellationToken)
    {
        var domainRequest = new BacktestRequest(
            request.InstrumentId, request.StrategyName, request.From, request.To,
            request.LookbackSamples, request.QuantityPerSignal, request.TakeProfitPercent,
            request.StopLossPercent, request.FeePerLot, request.StartingCapital, request.MomentumThresholdPercent);

        try
        {
            var result = await runner.RunAsync(domainRequest, cancellationToken);
            return Ok(result.ToResponse());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }
}
