using MarketsEngine.Api.Contracts;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.LiveTrading;
using MarketsEngine.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Api.Controllers;

/// <summary>
/// Real-money order approval workflow. Deliberately a separate controller from BrokerController
/// (whose own doc comment says no order-placement endpoint should ever be added there) and from
/// OrdersController (paper). Nothing here reaches Kite until POST /{id}/approve is called.
/// </summary>
[ApiController]
[Route("api/live-orders")]
public class LiveOrdersController(ILiveOrderExecutionService liveOrderExecutionService, MarketsEngineDbContext dbContext) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitLiveOrderApiRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OrderSide>(request.Side, ignoreCase: true, out var side))
            return BadRequest(new { message = $"Invalid side '{request.Side}'. Expected 'Buy' or 'Sell'." });

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return BadRequest(new { message = "IdempotencyKey is required." });

        var domainRequest = new SubmitLiveOrderRequest(request.InstrumentId, side, request.Quantity, request.IdempotencyKey, "Manual");
        var result = await liveOrderExecutionService.SubmitOrderAsync(domainRequest, cancellationToken);

        if (result.Success && result.Order is not null)
            return CreatedAtAction(nameof(GetById), new { id = result.Order.Id }, result.Order.ToResponse());

        return result.ErrorCode switch
        {
            "InstrumentNotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
            _ => UnprocessableEntity(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<LiveOrderResponse>>> List([FromQuery] string? status, CancellationToken cancellationToken)
    {
        var query = dbContext.LiveOrders.Include(o => o.Fills).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<LiveOrderStatus>(status, ignoreCase: true, out var parsedStatus))
                return BadRequest(new { message = $"Invalid status '{status}'." });

            query = query.Where(o => o.Status == parsedStatus);
        }

        var orders = await query.OrderByDescending(o => o.RequestedAtUtc).ToListAsync(cancellationToken);
        return Ok(orders.Select(o => o.ToResponse()).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LiveOrderResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await dbContext.LiveOrders
            .Include(o => o.Fills)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null ? NotFound() : Ok(order.ToResponse());
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var result = await liveOrderExecutionService.ApproveAsync(id, cancellationToken);

        if (result.Success && result.Order is not null)
            return Ok(result.Order.ToResponse());

        return result.ErrorCode == "OrderNotFound"
            ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
            : UnprocessableEntity(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLiveOrderApiRequest request, CancellationToken cancellationToken)
    {
        var result = await liveOrderExecutionService.RejectAsync(id, request.Reason, cancellationToken);

        if (result.Success && result.Order is not null)
            return Ok(result.Order.ToResponse());

        return result.ErrorCode == "OrderNotFound"
            ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
            : UnprocessableEntity(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await liveOrderExecutionService.CancelAsync(id, cancellationToken);

        if (result.Success && result.Order is not null)
            return Ok(result.Order.ToResponse());

        return result.ErrorCode == "OrderNotFound"
            ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
            : UnprocessableEntity(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
    }

    [HttpPost("{id:guid}/modify")]
    public async Task<IActionResult> Modify(Guid id, [FromBody] ModifyLiveOrderApiRequest request, CancellationToken cancellationToken)
    {
        var result = await liveOrderExecutionService.ModifyAsync(id, request.Quantity, cancellationToken);

        if (result.Success && result.Order is not null)
            return Ok(result.Order.ToResponse());

        return result.ErrorCode == "OrderNotFound"
            ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
            : UnprocessableEntity(new { errorCode = result.ErrorCode, message = result.ErrorMessage });
    }

    [HttpGet("positions")]
    public async Task<ActionResult<List<LivePositionResponse>>> GetPositions(CancellationToken cancellationToken)
    {
        var positions = await dbContext.LivePositions
            .Where(p => p.NetQuantity != 0)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Ok(positions.Select(p => p.ToResponse()).ToList());
    }
}
