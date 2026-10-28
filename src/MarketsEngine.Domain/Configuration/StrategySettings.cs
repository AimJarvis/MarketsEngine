using System.ComponentModel.DataAnnotations;

namespace MarketsEngine.Domain.Configuration;

/// <summary>
/// Controls the automated strategy runner. Off by default - automated order submission is an
/// opt-in, not something that starts just because the app does.
/// </summary>
public class StrategySettings
{
    public const string SectionName = "Strategy";

    public bool Enabled { get; set; } = false;

    /// <summary>Which IStrategy implementation to run: "SimpleMomentum" or "Kronos". The flag.</summary>
    [AllowedValues("SimpleMomentum", "Kronos")]
    public string ActiveStrategy { get; set; } = "SimpleMomentum";

    /// <summary>Which instruments the strategy trades: "Option", "Equity", or "All". The other flag.</summary>
    [AllowedValues("Option", "Equity", "All")]
    public string InstrumentKindFilter { get; set; } = "Option";

    /// <summary>The paper account the runner trades from. Required when Enabled is true.</summary>
    public Guid? AccountId { get; set; }

    [Range(1, int.MaxValue)]
    public int EvaluationIntervalSeconds { get; set; } = 30;

    [Range(2, 100)]
    public int LookbackSamples { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int QuantityPerSignal { get; set; } = 1;

    /// <summary>
    /// Minimum minutes between two auto-submitted buys on the same instrument, enforced by
    /// bucketing the idempotency key - reuses the existing duplicate-order protection instead
    /// of needing separate cooldown-tracking state.
    /// </summary>
    [Range(1, 1440)]
    public int CooldownMinutes { get; set; } = 10;

    [Range(0, 100)]
    public decimal MomentumThresholdPercent { get; set; } = 1.0m;

    /// <summary>Base URL of the Kronos forecasting service, e.g. http://localhost:8000. Only needed when ActiveStrategy is "Kronos".</summary>
    public string? KronosServiceUrl { get; set; }

    [Range(1, 100)]
    public int KronosForecastHorizon { get; set; } = 12;

    [Range(0, 100)]
    public decimal KronosDirectionThresholdPercent { get; set; } = 0.3m;

    /// <summary>
    /// Scales Kronos's own forecast magnitude (predicted % move) into a take-profit/stop-loss
    /// percent for the position it opens, instead of using the global default below. E.g. a
    /// forecast of +2% with TakeProfitFactor=1.0/StopLossFactor=0.5 sets take-profit at 2% and
    /// stop-loss at 1% for that position specifically. Deliberately simple (a fixed scale of the
    /// forecast, not a real risk model) - the point is only that Kronos has a basis to size its
    /// own exit, where SimpleMomentum does not.
    /// </summary>
    [Range(0, 10)]
    public decimal KronosTakeProfitFactor { get; set; } = 1.0m;

    [Range(0, 10)]
    public decimal KronosStopLossFactor { get; set; } = 0.5m;

    /// <summary>
    /// Risk management: applies to every open position on the watched account, regardless of
    /// InstrumentKindFilter or which entry strategy opened it - switching the entry strategy or
    /// filter should never silently stop protecting an existing position. This is the fallback
    /// used when the entry strategy's signal didn't specify its own StopLossPercent/TakeProfitPercent
    /// (see StrategySignal) - a per-position override from the strategy takes precedence over this.
    /// </summary>
    [Range(0, 1000)]
    public decimal TakeProfitPercent { get; set; } = 10.0m;

    [Range(0, 100)]
    public decimal StopLossPercent { get; set; } = 5.0m;
}
