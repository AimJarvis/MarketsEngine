using MarketsEngine.Api.Contracts;
using MarketsEngine.Infrastructure.Brokers;
using Microsoft.AspNetCore.Mvc;

namespace MarketsEngine.Api.Controllers;

/// <summary>
/// Zerodha Kite Connect integration - strictly read-only (login, instrument master, live
/// quotes). No order-placement endpoint exists here, and none should ever be added: paper
/// trading must stay paper trading.
/// </summary>
[ApiController]
[Route("api/broker/kite")]
public class BrokerController(
    KiteClientFactory clientFactory,
    KiteSessionStore sessionStore,
    KiteInstrumentSyncService instrumentSyncService,
    KiteTickerService tickerService,
    KiteHistoricalDataService historicalDataService) : ControllerBase
{
    [HttpGet("login-url")]
    public ActionResult<LoginUrlResponse> GetLoginUrl()
    {
        try
        {
            var kite = clientFactory.CreateUnauthenticatedClient();
            return Ok(new LoginUrlResponse(kite.GetLoginURL()));
        }
        catch (KiteNotConfiguredException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("callback")]
    public async Task<ActionResult<KiteCallbackResponse>> Callback(
        [FromQuery(Name = "request_token")] string? requestToken,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = $"Kite login did not succeed (status: {status ?? "unknown"})." });
        }

        if (string.IsNullOrWhiteSpace(requestToken))
        {
            return BadRequest(new { message = "Missing request_token." });
        }

        try
        {
            var kite = clientFactory.CreateUnauthenticatedClient();
            var user = kite.GenerateSession(requestToken, clientFactory.ApiSecret);

            await sessionStore.SaveAsync(user.AccessToken, user.PublicToken, user.UserId, cancellationToken);

            return Ok(new KiteCallbackResponse(true, user.UserId, DateTime.UtcNow));
        }
        catch (KiteNotConfiguredException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<BrokerStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetAsync(cancellationToken);

        return Ok(new BrokerStatusResponse(
            IsAuthenticated: session is not null,
            BrokerUserId: session?.BrokerUserId,
            SessionGeneratedAtUtc: session?.GeneratedAtUtc,
            IsTickerConnected: tickerService.IsConnected));
    }

    [HttpPost("sync-instruments")]
    public async Task<ActionResult<SyncInstrumentsResponse>> SyncInstruments(CancellationToken cancellationToken)
    {
        try
        {
            var count = await instrumentSyncService.SyncNiftyBankNiftyOptionsAsync(cancellationToken);
            return Ok(new SyncInstrumentsResponse(count));
        }
        catch (KiteNotConfiguredException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sync-equities")]
    public async Task<ActionResult<SyncInstrumentsResponse>> SyncEquities(CancellationToken cancellationToken)
    {
        try
        {
            var count = await instrumentSyncService.SyncEquitiesAsync(cancellationToken);
            return Ok(new SyncInstrumentsResponse(count));
        }
        catch (KiteNotConfiguredException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("ticker/start")]
    public async Task<IActionResult> StartTicker(CancellationToken cancellationToken)
    {
        try
        {
            await tickerService.StartAsync(cancellationToken);
            return Ok(new { message = "Kite ticker started." });
        }
        catch (KiteNotConfiguredException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("ticker/stop")]
    public IActionResult StopTicker()
    {
        tickerService.Stop();
        return Ok(new { message = "Kite ticker stopped." });
    }

    [HttpPost("import-historical")]
    public async Task<ActionResult<ImportHistoricalDataResponse>> ImportHistorical(
        [FromBody] ImportHistoricalDataApiRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var count = await historicalDataService.ImportHistoricalDataAsync(request.InstrumentId, request.From, request.To, cancellationToken);
            return Ok(new ImportHistoricalDataResponse(count));
        }
        catch (KiteNotConfiguredException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
