using System.ComponentModel.DataAnnotations;

namespace MarketsEngine.Domain.Configuration;

/// <summary>Off by default - WhatsApp alerts are opt-in, not automatic.</summary>
public class PnLAlertSettings
{
    public const string SectionName = "PnLAlerts";

    public bool Enabled { get; set; } = false;

    [Range(1, 1440)]
    public int CheckIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Only send a new alert once net P&amp;L has moved by at least this much (in rupees) since
    /// the last alert for that account. Set to 0 to alert on every check regardless of movement.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal AlertThresholdAmount { get; set; } = 500m;

    /// <summary>Which accounts to watch. Empty means every account.</summary>
    public Guid[] AccountIds { get; set; } = [];
}
