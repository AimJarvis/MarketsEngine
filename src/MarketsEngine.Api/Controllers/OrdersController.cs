using MarketsEngine.Api.Contracts;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Trading;
using MarketsEngine.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(IOrderExecutionService orderExecutionService, MarketsEngineDbContext dbContext) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitOrderApiRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OrderSide>(request.Side, ignoreCase: true, out var side))
        {
            return BadRequest(new { message = $"Invalid side '{request.Side}'. Expected 'Buy' or 'Sell'." });
        }

        if (!Enum.TryParse<TradingStyle>(request.TradingStyle, ignoreCase: true, out var tradingStyle))
        {
            return BadRequest(new { message = $"Invalid trading style '{request.TradingStyle}'. Expected 'Intraday' or 'Positional'." });
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return BadRequest(new { message = "IdempotencyKey is required." });
        }

        var domainRequest = new SubmitOrderRequest(
            request.AccountId, request.InstrumentId, side, tradingStyle, request.Quantity, request.IdempotencyKey);

        var result = await orderExecutionService.SubmitOrderAsync(domainRequest, cancellationToken);

        if (result.Success && result.Order is not null)
        {
            return CreatedAtAction(nameof(GetById), new { id = result.Order.Id }, result.Order.ToResponse());
        }

        return result.ErrorCode switch
        {
            "AccountNotFound" or "InstrumentNotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
            _ => UnprocessableEntity(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await dbContext.PaperOrders
            .Include(o => o.Fill)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null ? NotFound() : Ok(order.ToResponse());
    }
}
