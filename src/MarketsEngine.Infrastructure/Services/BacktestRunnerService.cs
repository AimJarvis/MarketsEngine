using MarketsEngine.Domain.Backtesting;
using MarketsEngine.Domain.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MarketsEngine.Infrastructure.Services;

/// <summary>
/// Loads imported HistoricalBar rows for one instrument/date range, resolves the requested
/// strategy, and hands both to the pure BacktestEngine. Requires historical data to already be
/// imported (see KiteHistoricalDataService) - this service has no fallback data source.
/// </summary>
public class BacktestRunnerService(MarketsEngineDbContext dbContext, IServiceProvider serviceProvider)
{
    private const string DailyInterval = "day";

    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        var instrument = await dbContext.Instruments.FirstOrDefaultAsync(i => i.Id == request.InstrumentId, cancellationToken)
            ?? throw new InvalidOperationException("Instrument not found.");

        var fromUtc = DateTime.SpecifyKind(request.From.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(request.To.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var bars = await dbContext.HistoricalBars
            .Where(b => b.InstrumentId == request.InstrumentId && b.Interval == DailyInterval
                        && b.TimestampUtc >= fromUtc && b.TimestampUtc <= toUtc)
            .OrderBy(b => b.TimestampUtc)
            .ToListAsync(cancellationToken);

        if (bars.Count == 0)
        {
            throw new InvalidOperationException(
                "No historical bars found for this instrument/date range. Import first: " +
                "POST /api/broker/kite/import-historical.");
        }

        IStrategy strategy = request.StrategyName switch
        {
            "SimpleMomentum" => new SimpleMomentumStrategy(request.MomentumThresholdPercent ?? 1.0m),
            "Kronos" => serviceProvider.GetRequiredKeyedService<IStrategy>("Kronos"),
            _ => throw new ArgumentException($"Unknown strategy '{request.StrategyName}'. Expected 'SimpleMomentum' or 'Kronos'.")
        };

        var backtestBars = bars
            .Select(b => new BacktestBar(b.TimestampUtc, b.Open, b.High, b.Low, b.Close, b.Volume))
            .ToList();

        var config = new BacktestConfig(
            LotSize: instrument.LotSize,
            QuantityPerSignal: request.QuantityPerSignal,
            LookbackSamples: request.LookbackSamples,
            TakeProfitPercent: request.TakeProfitPercent,
            StopLossPercent: request.StopLossPercent,
            FeePerLot: request.FeePerLot,
            StartingCapital: request.StartingCapital);

        return await BacktestEngine.RunAsync(
            instrument.Id, instrument.TradingSymbol, backtestBars, strategy, config, cancellationToken);
    }
}
