using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Ez.Handball.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class NotificationPublisherTests
{
    private static Notification Sample(NotificationType type = NotificationType.RoundResult)
        => new("u-1", type, "Title", "Body");

    private static NotificationPublisher Sut(
        NotificationPreferences? stored,
        params INotificationChannel[] channels)
    {
        var repo = new Mock<INotificationPreferenceRepository>();
        repo.Setup(r => r.GetAsync("u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        return new NotificationPublisher(repo.Object, channels, NullLogger<NotificationPublisher>.Instance);
    }

    private static NotificationPreferences Prefs(params (NotificationType, NotificationChannel)[] cells)
        => new("u-1", new HashSet<(NotificationType, NotificationChannel)>(cells));

    [Fact]
    public async Task DeliversToChannel_WhenCellEnabled()
    {
        var inApp = new InMemoryNotificationChannel(NotificationChannel.InApp);

        await Sut(Prefs((NotificationType.RoundResult, NotificationChannel.InApp)), inApp)
            .PublishAsync(Sample(), default);

        Assert.Single(inApp.Received);
    }

    [Fact]
    public async Task SkipsChannel_WhenCellDisabled()
    {
        var email = new InMemoryNotificationChannel(NotificationChannel.Email);

        // RoundResult enabled on InApp only — email cell absent.
        await Sut(Prefs((NotificationType.RoundResult, NotificationChannel.InApp)), email)
            .PublishAsync(Sample(), default);

        Assert.Empty(email.Received);
    }

    [Fact]
    public async Task FallsBackToDefaults_WhenNoStoredPreferences()
    {
        var inApp = new InMemoryNotificationChannel(NotificationChannel.InApp);

        await Sut(stored: null, inApp).PublishAsync(Sample(), default);

        Assert.Single(inApp.Received); // Default() enables InApp for all types
    }

    [Fact]
    public async Task OneThrowingChannel_DoesNotBlockOthers()
    {
        var good = new InMemoryNotificationChannel(NotificationChannel.InApp);
        var bad = new ThrowingChannel(NotificationChannel.Email);

        await Sut(Prefs(
                    (NotificationType.RoundResult, NotificationChannel.InApp),
                    (NotificationType.RoundResult, NotificationChannel.Email)),
                 bad, good)
            .PublishAsync(Sample(), default);

        Assert.Single(good.Received); // good delivered despite bad throwing
    }

    [Fact]
    public async Task SendsNothing_WhenStoredPreferencesAreEmpty_NoDefaultFallback()
    {
        var inApp = new InMemoryNotificationChannel(NotificationChannel.InApp);

        // Non-null empty prefs = "configured to receive nothing" — must NOT fall back to Default.
        await Sut(Prefs(), inApp).PublishAsync(Sample(), default);

        Assert.Empty(inApp.Received);
    }

    [Fact]
    public async Task DeliversToAllEnabledChannels_ForSameType()
    {
        var inApp = new InMemoryNotificationChannel(NotificationChannel.InApp);
        var email = new InMemoryNotificationChannel(NotificationChannel.Email);

        await Sut(Prefs(
                    (NotificationType.RoundResult, NotificationChannel.InApp),
                    (NotificationType.RoundResult, NotificationChannel.Email)),
                 inApp, email)
            .PublishAsync(Sample(), default);

        Assert.Single(inApp.Received);
        Assert.Single(email.Received);
    }

    [Fact]
    public async Task Propagates_WhenChannelIsCanceled()
    {
        var canceling = new CancelingChannel(NotificationChannel.InApp);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Sut(Prefs((NotificationType.RoundResult, NotificationChannel.InApp)), canceling)
                .PublishAsync(Sample(), default));
    }

    private sealed class ThrowingChannel : INotificationChannel
    {
        public ThrowingChannel(NotificationChannel channel) => Channel = channel;
        public NotificationChannel Channel { get; }
        public Task SendAsync(Notification notification, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    private sealed class CancelingChannel : INotificationChannel
    {
        public CancelingChannel(NotificationChannel channel) => Channel = channel;
        public NotificationChannel Channel { get; }
        public Task SendAsync(Notification notification, CancellationToken ct)
            => throw new OperationCanceledException();
    }
}
