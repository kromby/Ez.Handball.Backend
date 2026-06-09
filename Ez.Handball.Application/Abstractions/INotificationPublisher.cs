using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// The producer. Application code calls this to emit a notification; the implementation
// resolves preferences and fans out to enabled channels.
public interface INotificationPublisher
{
    Task PublishAsync(Notification notification, CancellationToken ct);
}
