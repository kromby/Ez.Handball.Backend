using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface INotificationPreferenceRepository
{
    // Returns null when the user has never stored any preference (caller falls back to
    // NotificationPreferences.Default). An empty-but-non-null result means "configured
    // to receive nothing".
    Task<NotificationPreferences?> GetAsync(string userId, CancellationToken ct);

    Task UpsertAsync(NotificationPreferences preferences, CancellationToken ct);
}
