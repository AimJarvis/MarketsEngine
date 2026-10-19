namespace MarketsEngine.Domain.Strategies;

public interface IStrategy
{
    Task<StrategySignal> EvaluateAsync(StrategyContext context, CancellationToken cancellationToken = default);
}
