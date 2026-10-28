using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Infrastructure.Health;

/// <summary>
/// Watches HealthStatusTracker and flags a background service whose heartbeat has gone stale
/// well beyond its own expected interval - a real crash/hang, not just "feature is disabled"
/// (heartbeats are recorded every loop iteration regardless of the feature's own Enabled flag).
/// Always runs (not gated by any Enabled flag itself - a watchdog that can be turned off isn't
/// much of a watchdog). Alerts are best-effort via the existing IWhatsAppNotifier/Twilio pipeline
/// when configured; the file/console log (see Program.cs's Serilog setup) is the always-available
/// record regardless of whether Twilio is set up.
/// </summary>
public class HealthMonitorService(
    IServiceScopeFactory scopeFactory,
    HealthStatusTracker tracker,
    IOptionsMonitor<StrategySettings> strategySettingsMonitor,
    IOptionsMonitor<LiveTradingSettings> liveTradingSettingsMonitor,
    IOptionsMonitor<PnLAlertSettings> pnlAlertSettingsMonitor,
    ILogger<HealthMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan GraceExtra = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, bool> _currentlyStale = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckServiceAsync(
                    "StrategyRunnerService",
                    TimeSpan.FromSeconds(strategySettingsMonitor.CurrentValue.EvaluationIntervalSeconds),
                    stoppingToken);

                await CheckServiceAsync(
                    "LiveOrderReconciliationService",
                    TimeSpan.FromSeconds(liveTradingSettingsMonitor.CurrentValue.ReconciliationIntervalSeconds),
                    stoppingToken);

                await CheckServiceAsync(
                    "PnLAlertService",
                    TimeSpan.FromMinutes(pnlAlertSettingsMonitor.CurrentValue.CheckIntervalMinutes),
                    stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health monitor check failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task CheckServiceAsync(string serviceName, TimeSpan expectedInterval, CancellationToken cancellationToken)
    {
        if (!tracker.TryGetLastHeartbeat(serviceName, out var lastHeartbeatUtc))
        {
            // Hasn't completed its first loop iteration yet (e.g. app just started) - not stale.
            return;
        }

        var staleThreshold = (expectedInterval * 3) + GraceExtra;
        var timeSinceHeartbeat = DateTime.UtcNow - lastHeartbeatUtc;
        var isStale = timeSinceHeartbeat > staleThreshold;
        var wasStale = _currentlyStale.GetValueOrDefault(serviceName);

        if (isStale && !wasStale)
        {
            logger.LogError(
                "{Service} has not reported a heartbeat in {Elapsed} (expected every ~{Expected}) - it may have crashed or hung.",
                serviceName, timeSinceHeartbeat, expectedInterval);

            await TryAlertAsync(serviceName, timeSinceHeartbeat, cancellationToken);
            _currentlyStale[serviceName] = true;
        }
        else if (!isStale && wasStale)
        {
            logger.LogInformation("{Service} heartbeat recovered.", serviceName);
            _currentlyStale[serviceName] = false;
        }
    }

    private async Task TryAlertAsync(string serviceName, TimeSpan timeSinceHeartbeat, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var notifier = scope.ServiceProvider.GetRequiredService<IWhatsAppNotifier>();

            await notifier.SendAsync(
                $"MarketsEngine health alert: {serviceName} has not reported in for {timeSinceHeartbeat:hh\\:mm\\:ss} - it may have crashed or hung.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort only - Twilio may not be configured. The error-level log above is the
            // guaranteed record regardless of whether this alert channel is available.
            logger.LogWarning(ex, "Could not send WhatsApp health alert for {Service} (Twilio may not be configured)", serviceName);
        }
    }
}
