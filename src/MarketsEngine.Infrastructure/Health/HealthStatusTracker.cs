using System.Collections.Concurrent;

namespace MarketsEngine.Infrastructure.Health;

/// <summary>
/// Shared in-memory record of "last time each background service completed a loop iteration",
/// regardless of whether that iteration's feature flag was enabled or did any actual work - a
/// heartbeat means the service's loop is alive, not that it traded/alerted/reconciled anything.
/// HealthMonitorService uses this to detect a hung/crashed background service, which is a
/// different failure mode from "feature is just turned off".
/// </summary>
public class HealthStatusTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeatUtc = new();

    public void RecordHeartbeat(string serviceName) => _lastHeartbeatUtc[serviceName] = DateTime.UtcNow;

    public bool TryGetLastHeartbeat(string serviceName, out DateTime lastHeartbeatUtc) =>
        _lastHeartbeatUtc.TryGetValue(serviceName, out lastHeartbeatUtc);
}
