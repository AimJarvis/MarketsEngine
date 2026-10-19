namespace MarketsEngine.Domain.Strategies;

/// <summary>
/// V1 strategies only ever signal "buy more of this instrument" - never a sell/short signal.
/// Buying options has a capped max loss (the premium paid), matching the project's stated
/// preference for defined-risk strategies; automated short-selling is explicitly out of scope.
/// </summary>
public record StrategySignal(bool ShouldBuy, string Reason)
{
    public static readonly StrategySignal None = new(false, "No signal");
}
