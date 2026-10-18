using MarketsEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Infrastructure;

public class MarketsEngineDbContext(DbContextOptions<MarketsEngineDbContext> options) : DbContext(options)
{
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<MarketQuote> MarketQuotes => Set<MarketQuote>();
    public DbSet<PaperAccount> PaperAccounts => Set<PaperAccount>();
    public DbSet<PaperOrder> PaperOrders => Set<PaperOrder>();
    public DbSet<PaperFill> PaperFills => Set<PaperFill>();
    public DbSet<PaperPosition> PaperPositions => Set<PaperPosition>();
    public DbSet<CashLedgerEntry> CashLedgerEntries => Set<CashLedgerEntry>();
    public DbSet<BrokerSession> BrokerSessions => Set<BrokerSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MarketsEngineDbContext).Assembly);
    }
}
