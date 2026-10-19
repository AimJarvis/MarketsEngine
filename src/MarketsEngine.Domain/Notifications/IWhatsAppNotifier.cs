namespace MarketsEngine.Domain.Notifications;

public interface IWhatsAppNotifier
{
    Task SendAsync(string message, CancellationToken cancellationToken = default);
}
