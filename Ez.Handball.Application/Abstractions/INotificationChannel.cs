using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// A delivery channel (consumer). One implementation per NotificationChannel value.
public interface INotificationChannel
{
    NotificationChannel Channel { get; }
    Task SendAsync(Notification notification, CancellationToken ct);
}
