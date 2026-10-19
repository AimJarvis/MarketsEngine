using MarketsEngine.Api.Contracts;
using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(MarketsEngineDbContext dbContext, AccountPnLService pnLService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create([FromBody] CreateAccountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }

        var now = DateTime.UtcNow;
        var account = new PaperAccount
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            CashBalance = request.InitialCapital ?? 50_000m,
            ReservedMargin = 0m,
            CreatedAtUtc = now
        };

        dbContext.PaperAccounts.Add(account);
        dbContext.CashLedgerEntries.Add(new CashLedgerEntry
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            EntryType = CashLedgerEntryType.InitialDeposit,
            Amount = account.CashBalance,
            BalanceAfter = account.CashBalance,
            Description = "Initial simulated capital",
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = account.Id }, account.ToResponse());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.PaperAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return account is null ? NotFound() : Ok(account.ToResponse());
    }

    [HttpGet("{id:guid}/positions")]
    public async Task<ActionResult<IEnumerable<PositionResponse>>> GetPositions(Guid id, CancellationToken cancellationToken)
    {
        if (!await dbContext.PaperAccounts.AnyAsync(a => a.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var positions = await dbContext.PaperPositions
            .Where(p => p.AccountId == id && p.NetQuantity != 0)
            .Include(p => p.Instrument)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var responses = new List<PositionResponse>();
        foreach (var position in positions)
        {
            var unrealizedPnL = await pnLService.CalculateUnrealizedPnLAsync(position, cancellationToken);
            responses.Add(new PositionResponse(
                position.InstrumentId, position.Instrument!.TradingSymbol, position.NetQuantity,
                position.AverageEntryPrice, position.RealizedPnL, unrealizedPnL, position.LastUpdatedAtUtc));
        }

        return Ok(responses);
    }

    [HttpGet("{id:guid}/pnl")]
    public async Task<ActionResult<AccountPnLResponse>> GetPnL(Guid id, CancellationToken cancellationToken)
    {
        if (!await dbContext.PaperAccounts.AnyAsync(a => a.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var (realized, unrealized, net) = await pnLService.GetTotalPnLAsync(id, cancellationToken);

        return Ok(new AccountPnLResponse(id, realized, unrealized, net));
    }

    [HttpGet("{id:guid}/orders")]
    public async Task<ActionResult<IEnumerable<OrderResponse>>> GetOrders(Guid id, CancellationToken cancellationToken)
    {
        if (!await dbContext.PaperAccounts.AnyAsync(a => a.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var orders = await dbContext.PaperOrders
            .Where(o => o.AccountId == id)
            .Include(o => o.Fill)
            .OrderByDescending(o => o.RequestedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Ok(orders.Select(o => o.ToResponse()));
    }
}
