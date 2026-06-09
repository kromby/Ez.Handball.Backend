using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Fakes;

// Records every notification it receives. Stands in for a real channel in tests.
public sealed class InMemoryNotificationChannel : INotificationChannel
{
    private readonly List<Notification> _received = new();

    public InMemoryNotificationChannel(NotificationChannel channel) => Channel = channel;

    public NotificationChannel Channel { get; }

    public IReadOnlyList<Notification> Received => _received;

    public Task SendAsync(Notification notification, CancellationToken ct)
    {
        _received.Add(notification);
        return Task.CompletedTask;
    }
}
