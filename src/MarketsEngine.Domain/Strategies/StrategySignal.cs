namespace MarketsEngine.Domain.Strategies;

/// <summary>
/// V1 strategies only ever signal "buy more of this instrument" - never a sell/short signal.
/// Buying options has a capped max loss (the premium paid), matching the project's stated
/// preference for defined-risk strategies; automated short-selling is explicitly out of scope.
///
/// StopLossPercent/TakeProfitPercent let a strategy that has a basis for one (e.g. a forecast
/// magnitude) override the global Strategy:StopLossPercent/TakeProfitPercent default for the
/// position this signal opens. Null means "no opinion, use the global default" - a strategy with
/// no such basis (SimpleMomentum) should leave these null rather than invent a number.
/// </summary>
public record StrategySignal(bool ShouldBuy, string Reason, decimal? StopLossPercent = null, decimal? TakeProfitPercent = null)
{
    public static readonly StrategySignal None = new(false, "No signal");
}
