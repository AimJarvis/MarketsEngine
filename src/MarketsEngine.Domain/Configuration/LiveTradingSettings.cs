using System.ComponentModel.DataAnnotations;

namespace MarketsEngine.Domain.Configuration;

/// <summary>
/// Controls real-money order execution via Kite Connect. Off by default - this is the
/// separate, real-money kill switch, distinct from Trading:PaperTradingEnabled, and doubles as
/// the paper/live mode switch read by TradingOrderGateway. Validated at startup like
/// TradingSettings/StrategySettings (these are internal safety numbers, not optional
/// credentials, so they must fail fast if misconfigured).
/// </summary>
public class LiveTradingSettings
{
    public const string SectionName = "LiveTrading";

    public bool Enabled { get; set; } = false;

    /// <summary>
    /// When true (default), a validated signal is parked as PendingApproval and never reaches
    /// Kite until a human explicitly approves it. Kept configurable so full-auto can be enabled
    /// later purely by config, without a code change.
    /// </summary>
    public bool RequireManualApproval { get; set; } = true;

    [AllowedValues("MIS", "NRML")]
    public string DefaultProduct { get; set; } = "MIS";

    [Range(1, int.MaxValue)]
    public int MaxQuantityPerOrder { get; set; } = 1;

    [Range(0, double.MaxValue)]
    public decimal MaxNotionalPerOrder { get; set; } = 10000m;

    [Range(0, double.MaxValue)]
    public decimal MaxNotionalPerDay { get; set; } = 25000m;

    [Range(5, 3600)]
    public int ReconciliationIntervalSeconds { get; set; } = 15;

    /// <summary>A PendingApproval order older than this is auto-rejected - an approval must never fire against a stale price.</summary>
    [Range(1, 1440)]
    public int PendingApprovalExpiryMinutes { get; set; } = 30;
}
