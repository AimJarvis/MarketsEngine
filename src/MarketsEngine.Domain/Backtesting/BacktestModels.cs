using MarketsEngine.Domain.Enums;

namespace MarketsEngine.Domain.Backtesting;

/// <summary>One OHLC bar as the backtest engine needs it - decoupled from the EF entity so this namespace has no DB dependency.</summary>
public record BacktestBar(DateTime TimestampUtc, decimal Open, decimal High, decimal Low, decimal Close);

/// <summary>
/// MomentumThresholdPercent only applies when StrategyName="SimpleMomentum" - it's constructed
/// fresh per request so you can experiment with thresholds without touching live Strategy:*
/// config. Kronos has no per-request override (it's resolved via the existing keyed DI
/// registration, so its forecast parameters follow whatever Strategy:Kronos* is configured).
/// </summary>
public record BacktestRequest(
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

public record BacktestConfig(
    int LotSize,
    int QuantityPerSignal,
    int LookbackSamples,
    decimal TakeProfitPercent,
    decimal StopLossPercent,
    decimal FeePerLot,
    decimal StartingCapital);

public record BacktestTrade(
    OrderSide Side,
    DateTime EntryAtUtc,
    decimal EntryPrice,
    int Quantity,
    DateTime? ExitAtUtc,
    decimal? ExitPrice,
    string? ExitReason,
    decimal? RealizedPnL,
    decimal Fees,
    decimal TakeProfitPercent,
    decimal StopLossPercent);

public record OpenPositionSummary(
    int NetQuantity, decimal AverageEntryPrice, decimal UnrealizedPnLAtEnd,
    decimal TakeProfitPercent, decimal StopLossPercent);

public record BacktestResult(
    DateTime FromUtc,
    DateTime ToUtc,
    int BarsEvaluated,
    decimal StartingCapital,
    decimal FinalEquity,
    decimal TotalRealizedPnL,
    decimal TotalFeesPaid,
    int TradeCount,
    int WinCount,
    int LossCount,
    decimal WinRatePercent,
    decimal MaxDrawdownPercent,
    IReadOnlyList<BacktestTrade> Trades,
    OpenPositionSummary? OpenPositionAtEnd);
