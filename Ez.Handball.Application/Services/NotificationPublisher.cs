using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Application.Services;

// In-process synchronous fan-out. Resolves the user's preferences, then invokes every
// channel whose (type, channel) cell is enabled. A failing channel is logged and skipped
// so it cannot block delivery to the others.
public sealed class NotificationPublisher : INotificationPublisher
{
    private readonly INotificationPreferenceRepository _preferences;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(
        INotificationPreferenceRepository preferences,
        IEnumerable<INotificationChannel> channels,
        ILogger<NotificationPublisher> logger)
    {
        _preferences = preferences;
        _channels = channels;
        _logger = logger;
    }

    public async Task PublishAsync(Notification notification, CancellationToken ct)
    {
        var prefs = await _preferences.GetAsync(notification.UserId, ct)
                    ?? NotificationPreferences.Default(notification.UserId);

        foreach (var channel in _channels)
        {
            if (!prefs.IsEnabled(notification.Type, channel.Channel))
                continue;

            try
            {
                await channel.SendAsync(notification, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Notification channel {Channel} failed for user {UserId}, type {Type}",
                    channel.Channel, notification.UserId, notification.Type);
            }
        }
    }
}
