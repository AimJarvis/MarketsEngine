using MarketsEngine.Domain.Backtesting;

namespace MarketsEngine.Api.Contracts;

public record ImportHistoricalDataApiRequest(Guid InstrumentId, DateOnly From, DateOnly To);

public record BacktestApiRequest(
    Guid InstrumentId,
    string StrategyName,
    DateOnly From,
    DateOnly To,
    int LookbackSamples,
    int QuantityPerSignal,
    decimal TakeProfitPercent,
    decimal StopLossPercent,
    decimal FeePerLot,
    decimal StartingCapital,
    decimal? MomentumThresholdPercent);

public record BacktestTradeResponse(
    string Side, DateTime EntryAtUtc, decimal EntryPrice, int Quantity,
    DateTime? ExitAtUtc, decimal? ExitPrice, string? ExitReason, decimal? RealizedPnL, decimal Fees);

public record OpenPositionResponse(int NetQuantity, decimal AverageEntryPrice, decimal UnrealizedPnLAtEnd);

public record BacktestResultResponse(
    DateTime FromUtc, DateTime ToUtc, int BarsEvaluated, decimal StartingCapital, decimal FinalEquity,
    decimal TotalRealizedPnL, decimal TotalFeesPaid, int TradeCount, int WinCount, int LossCount,
    decimal WinRatePercent, decimal MaxDrawdownPercent, List<BacktestTradeResponse> Trades,
    OpenPositionResponse? OpenPositionAtEnd);

public static class BacktestResponseMapping
{
    public static BacktestResultResponse ToResponse(this BacktestResult r) => new(
        r.FromUtc, r.ToUtc, r.BarsEvaluated, r.StartingCapital, r.FinalEquity, r.TotalRealizedPnL,
        r.TotalFeesPaid, r.TradeCount, r.WinCount, r.LossCount, r.WinRatePercent, r.MaxDrawdownPercent,
        r.Trades.Select(t => new BacktestTradeResponse(
            t.Side.ToString(), t.EntryAtUtc, t.EntryPrice, t.Quantity,
            t.ExitAtUtc, t.ExitPrice, t.ExitReason, t.RealizedPnL, t.Fees)).ToList(),
        r.OpenPositionAtEnd is null ? null : new OpenPositionResponse(
            r.OpenPositionAtEnd.NetQuantity, r.OpenPositionAtEnd.AverageEntryPrice, r.OpenPositionAtEnd.UnrealizedPnLAtEnd));
}
