using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Infrastructure.Brokers;

public class KiteSessionStore(MarketsEngineDbContext dbContext)
{
    private const string Provider = "Kite";

    public Task<BrokerSession?> GetAsync(CancellationToken cancellationToken = default) =>
        dbContext.BrokerSessions.FirstOrDefaultAsync(s => s.Provider == Provider, cancellationToken);

    public async Task SaveAsync(string accessToken, string? publicToken, string? brokerUserId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.BrokerSessions.FirstOrDefaultAsync(s => s.Provider == Provider, cancellationToken);

        if (session is null)
        {
            session = new BrokerSession { Id = Guid.NewGuid(), Provider = Provider };
            dbContext.BrokerSessions.Add(session);
        }

        session.AccessToken = accessToken;
        session.PublicToken = publicToken;
        session.BrokerUserId = brokerUserId;
        session.GeneratedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
