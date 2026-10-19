using MarketsEngine.Domain.Configuration;
using MarketsEngine.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketsEngine.Infrastructure.Notifications;

/// <summary>
/// Periodically checks each watched account's P&amp;L and sends a WhatsApp alert via Twilio when
/// it has moved by at least PnLAlerts:AlertThresholdAmount since the last alert for that
/// account. Off by default (PnLAlerts:Enabled) - alerts are opt-in.
/// </summary>
public class PnLAlertService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PnLAlertSettings> settingsMonitor,
    ILogger<PnLAlertService> logger) : BackgroundService
{
    private readonly Dictionary<Guid, decimal> _lastAlertedPnL = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = settingsMonitor.CurrentValue;

            if (settings.Enabled)
            {
                try
                {
                    await CheckOnceAsync(settings, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "P&L alert check failed");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(settings.CheckIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    public async Task CheckOnceAsync(PnLAlertSettings settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketsEngineDbContext>();
        var pnLService = scope.ServiceProvider.GetRequiredService<AccountPnLService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IWhatsAppNotifier>();

        var accountsQuery = dbContext.PaperAccounts.AsNoTracking();
        if (settings.AccountIds.Length > 0)
        {
            accountsQuery = accountsQuery.Where(a => settings.AccountIds.Contains(a.Id));
        }

        var accounts = await accountsQuery.ToListAsync(cancellationToken);

        foreach (var account in accounts)
        {
            try
            {
                var (realized, unrealized, net) = await pnLService.GetTotalPnLAsync(account.Id, cancellationToken);

                var hasAlertedBefore = _lastAlertedPnL.TryGetValue(account.Id, out var lastNet);
                var movedEnough = !hasAlertedBefore || Math.Abs(net - lastNet) >= settings.AlertThresholdAmount;

                if (!movedEnough)
                {
                    continue;
                }

                var emoji = net >= 0 ? "🟢" : "🔴";
                var message =
                    $"{emoji} MarketsEngine - {account.Name}\n" +
                    $"Net P&L: ₹{net:N2}\n" +
                    $"Realized: ₹{realized:N2} | Unrealized: ₹{unrealized:N2}\n" +
                    $"Cash balance: ₹{account.CashBalance:N2}";

                await notifier.SendAsync(message, cancellationToken);
                _lastAlertedPnL[account.Id] = net;

                logger.LogInformation("Sent P&L alert for account {AccountName}: net {Net:N2}", account.Name, net);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "P&L alert failed for account {AccountId}", account.Id);
            }
        }
    }
}
