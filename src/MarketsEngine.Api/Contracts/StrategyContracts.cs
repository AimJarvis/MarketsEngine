namespace MarketsEngine.Api.Contracts;

public record StrategyStatusResponse(
    bool Enabled, string ActiveStrategy, string InstrumentKindFilter, Guid? AccountId, int EvaluationIntervalSeconds);
