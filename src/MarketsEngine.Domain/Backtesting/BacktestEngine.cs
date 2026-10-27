using MarketsEngine.Domain.Enums;
using MarketsEngine.Domain.Strategies;
using MarketsEngine.Domain.Trading;

namespace MarketsEngine.Domain.Backtesting;

/// <summary>
/// Replays historical bars through a real IStrategy and the exact same pure calculators the live
/// paper/live engines use (PositionUpdater, PositionExitEvaluator, PnLCalculator) - no DB, no
/// HTTP of its own, so this is directly unit-testable. Fills happen exactly at bar.Close (no
/// bid/ask spread exists in historical OHLC data, so no slippage is modeled - this makes backtest
/// results somewhat optimistic versus live trading, a deliberate, documented simplification).
///
/// A position never spans more than one "trade" record in the result in an ambiguous way: since
/// strategies only ever signal buy and exits always close the full quantity, a trade's lifecycle
/// is cleanly flat -> one or more same-direction fills (averaging in) -> one closing fill back to
/// flat. Re-entry on the exact same bar an exit just fired is skipped (mirrors, in the simplest
/// possible bar-level form, the live CooldownMinutes fix for the same-pass re-entry race found
/// during live testing) - no separate cooldown config is modeled here.
/// </summary>
public static class BacktestEngine
{
    public static async Task<BacktestResult> RunAsync(
        Guid instrumentId,
        string tradingSymbol,
        IReadOnlyList<BacktestBar> bars,
        IStrategy strategy,
        BacktestConfig config,
        CancellationToken cancellationToken = default)
    {
        if (bars.Count == 0)
        {
            throw new ArgumentException("At least one historical bar is required to run a backtest.", nameof(bars));
        }

        var window = new Queue<PricePoint>();
        var netQuantity = 0;
        var averageEntryPrice = 0m;
        var totalRealizedPnL = 0m;
        var totalFeesPaid = 0m;
        var cycleFees = 0m;
        DateTime? cycleEntryAtUtc = null;
        var trades = new List<BacktestTrade>();
        var peakEquity = config.StartingCapital;
        var maxDrawdownPercent = 0m;

        foreach (var bar in bars)
        {
            cancellationToken.ThrowIfCancellationRequested();

            window.Enqueue(new PricePoint(bar.TimestampUtc, bar.Close));
            while (window.Count > config.LookbackSamples)
            {
                window.Dequeue();
            }

            var justExited = false;

            if (netQuantity != 0)
            {
                var exitReason = PositionExitEvaluator.Evaluate(
                    netQuantity, averageEntryPrice, bar.Close, config.LotSize, config.TakeProfitPercent, config.StopLossPercent);

                if (exitReason != PositionExitEvaluator.ExitReason.None)
                {
                    var closingSide = netQuantity > 0 ? OrderSide.Sell : OrderSide.Buy;
                    var closingQuantity = Math.Abs(netQuantity);
                    var fee = config.FeePerLot * closingQuantity;

                    var update = PositionUpdater.ApplyFill(netQuantity, averageEntryPrice, closingSide, closingQuantity, bar.Close, config.LotSize);

                    cycleFees += fee;
                    totalFeesPaid += fee;
                    totalRealizedPnL += update.RealizedPnLDelta;

                    trades.Add(new BacktestTrade(
                        Side: netQuantity > 0 ? OrderSide.Buy : OrderSide.Sell,
                        EntryAtUtc: cycleEntryAtUtc!.Value,
                        EntryPrice: averageEntryPrice,
                        Quantity: closingQuantity,
                        ExitAtUtc: bar.TimestampUtc,
                        ExitPrice: bar.Close,
                        ExitReason: exitReason.ToString(),
                        RealizedPnL: update.RealizedPnLDelta - cycleFees,
                        Fees: cycleFees));

                    netQuantity = update.NewNetQuantity;
                    averageEntryPrice = update.NewAverageEntryPrice;
                    cycleFees = 0m;
                    cycleEntryAtUtc = null;
                    justExited = true;
                }
            }

            if (!justExited)
            {
                var context = new StrategyContext(instrumentId, tradingSymbol, window.ToArray());
                var signal = await strategy.EvaluateAsync(context, cancellationToken);

                if (signal.ShouldBuy)
                {
                    var wasFlat = netQuantity == 0;
                    var fee = config.FeePerLot * config.QuantityPerSignal;

                    var update = PositionUpdater.ApplyFill(netQuantity, averageEntryPrice, OrderSide.Buy, config.QuantityPerSignal, bar.Close, config.LotSize);

                    if (wasFlat)
                    {
                        cycleEntryAtUtc = bar.TimestampUtc;
                    }

                    netQuantity = update.NewNetQuantity;
                    averageEntryPrice = update.NewAverageEntryPrice;
                    cycleFees += fee;
                    totalFeesPaid += fee;
                }
            }

            var unrealizedPnL = PnLCalculator.CalculateUnrealizedPnL(netQuantity, averageEntryPrice, bar.Close, config.LotSize);
            var currentEquity = config.StartingCapital + totalRealizedPnL - totalFeesPaid + unrealizedPnL;

            peakEquity = Math.Max(peakEquity, currentEquity);
            if (peakEquity > 0)
            {
                var drawdownPercent = (peakEquity - currentEquity) / peakEquity * 100m;
                maxDrawdownPercent = Math.Max(maxDrawdownPercent, drawdownPercent);
            }
        }

        var finalBar = bars[^1];
        var finalUnrealizedPnL = PnLCalculator.CalculateUnrealizedPnL(netQuantity, averageEntryPrice, finalBar.Close, config.LotSize);
        var finalEquity = config.StartingCapital + totalRealizedPnL - totalFeesPaid + finalUnrealizedPnL;

        var openPositionAtEnd = netQuantity != 0
            ? new OpenPositionSummary(netQuantity, averageEntryPrice, finalUnrealizedPnL)
            : null;

        var wins = trades.Count(t => t.RealizedPnL > 0);
        var losses = trades.Count(t => t.RealizedPnL <= 0);
        var winRatePercent = trades.Count > 0 ? (decimal)wins / trades.Count * 100m : 0m;

        return new BacktestResult(
            FromUtc: bars[0].TimestampUtc,
            ToUtc: finalBar.TimestampUtc,
            BarsEvaluated: bars.Count,
            StartingCapital: config.StartingCapital,
            FinalEquity: finalEquity,
            TotalRealizedPnL: totalRealizedPnL,
            TotalFeesPaid: totalFeesPaid,
            TradeCount: trades.Count,
            WinCount: wins,
            LossCount: losses,
            WinRatePercent: winRatePercent,
            MaxDrawdownPercent: maxDrawdownPercent,
            Trades: trades,
            OpenPositionAtEnd: openPositionAtEnd);
    }
}
