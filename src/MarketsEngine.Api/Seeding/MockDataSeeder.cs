using MarketsEngine.Domain.Entities;
using MarketsEngine.Domain.Enums;
using MarketsEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MarketsEngine.Api.Seeding;

/// <summary>
/// Dev-only convenience: applies pending migrations and seeds a handful of mock
/// NIFTY/BANKNIFTY instruments, quotes, and a starter paper account if the database is empty.
/// </summary>
public static class MockDataSeeder
{
    private const string DefaultAccountName = "Default Paper Account";
    private const decimal InitialCapital = 50_000m;

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();

        await dbContext.Database.MigrateAsync();

        if (!await dbContext.Instruments.AnyAsync())
        {
            SeedInstrumentsAndQuotes(dbContext);
        }

        if (!await dbContext.PaperAccounts.AnyAsync())
        {
            SeedDefaultAccount(dbContext);
        }

        await dbContext.SaveChangesAsync();
    }

    private static void SeedInstrumentsAndQuotes(MarketsEngineDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var expiry = DateOnly.FromDateTime(now).AddDays(14);

        var instruments = new[]
        {
            CreateInstrument("NIFTY", Underlying.Nifty, OptionType.Call, 24500m, expiry, lotSize: 75, now),
            CreateInstrument("NIFTY", Underlying.Nifty, OptionType.Put, 24500m, expiry, lotSize: 75, now),
            CreateInstrument("BANKNIFTY", Underlying.BankNifty, OptionType.Call, 51000m, expiry, lotSize: 30, now),
            CreateInstrument("BANKNIFTY", Underlying.BankNifty, OptionType.Put, 51000m, expiry, lotSize: 30, now)
        };

        dbContext.Instruments.AddRange(instruments);

        var mockPrices = new (decimal Bid, decimal Ask, decimal Ltp)[]
        {
            (180.00m, 182.50m, 181.00m),
            (165.00m, 167.50m, 166.00m),
            (420.00m, 424.00m, 422.00m),
            (395.00m, 399.00m, 397.00m)
        };

        for (var i = 0; i < instruments.Length; i++)
        {
            dbContext.MarketQuotes.Add(new MarketQuote
            {
                Id = Guid.NewGuid(),
                InstrumentId = instruments[i].Id,
                BidPrice = mockPrices[i].Bid,
                AskPrice = mockPrices[i].Ask,
                LastTradedPrice = mockPrices[i].Ltp,
                DataSource = "Mock",
                QuotedAtUtc = now
            });
        }
    }

    private static Instrument CreateInstrument(
        string underlyingSymbol, Underlying underlying, OptionType optionType,
        decimal strike, DateOnly expiry, int lotSize, DateTime now)
    {
        var optionLetter = optionType == OptionType.Call ? "CE" : "PE";
        var tradingSymbol = $"{underlyingSymbol}{expiry:yyMMdd}{strike:0}{optionLetter}";

        return new Instrument
        {
            Id = Guid.NewGuid(),
            TradingSymbol = tradingSymbol,
            Underlying = underlying,
            OptionType = optionType,
            StrikePrice = strike,
            ExpiryDate = expiry,
            LotSize = lotSize,
            DataSource = "Mock",
            CreatedAtUtc = now
        };
    }

    private static void SeedDefaultAccount(MarketsEngineDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var account = new PaperAccount
        {
            Id = Guid.NewGuid(),
            Name = DefaultAccountName,
            CashBalance = InitialCapital,
            ReservedMargin = 0m,
            CreatedAtUtc = now
        };

        dbContext.PaperAccounts.Add(account);

        dbContext.CashLedgerEntries.Add(new CashLedgerEntry
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            EntryType = CashLedgerEntryType.InitialDeposit,
            Amount = InitialCapital,
            BalanceAfter = InitialCapital,
            RelatedOrderId = null,
            Description = "Initial simulated capital",
            CreatedAtUtc = now
        });
    }
}
