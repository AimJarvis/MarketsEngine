using MarketsEngine.Api.Contracts;
using MarketsEngine.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Api.Controllers;

[ApiController]
[Route("api/instruments")]
public class InstrumentsController(MarketsEngineDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<InstrumentResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .OrderBy(i => i.TradingSymbol)
            .ToListAsync(cancellationToken);

        return Ok(instruments.Select(i => i.ToResponse()));
    }

    [HttpGet("{id:guid}/quote")]
    public async Task<ActionResult<QuoteResponse>> GetLatestQuote(Guid id, CancellationToken cancellationToken)
    {
        var quote = await dbContext.MarketQuotes
            .Where(q => q.InstrumentId == id)
            .OrderByDescending(q => q.QuotedAtUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (quote is null)
        {
            return NotFound(new { message = "No quote is available for this instrument." });
        }

        return Ok(quote.ToResponse());
    }
}
