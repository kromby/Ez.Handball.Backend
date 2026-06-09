using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure;

// Placeholder in-app channel: logs what it would deliver. No real provider yet (#18
// skeleton). Keeps the wired fan-out path observably alive in production.
internal sealed class LoggingNotificationChannel : INotificationChannel
{
    private readonly ILogger<LoggingNotificationChannel> _logger;

    public LoggingNotificationChannel(ILogger<LoggingNotificationChannel> logger) => _logger = logger;

    public NotificationChannel Channel => NotificationChannel.InApp;

    public Task SendAsync(Notification notification, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Log only the type — UserId and the free-text Title are kept out of info-level logs.
        _logger.LogInformation("[notification] type={Type}", notification.Type);
        return Task.CompletedTask;
    }
}
