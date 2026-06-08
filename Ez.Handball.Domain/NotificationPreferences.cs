namespace Ez.Handball.Domain;

// Per-user type×channel matrix. A cell present in Enabled means "deliver this type on
// this channel". Absent = disabled.
public sealed record NotificationPreferences(
    string UserId,
    IReadOnlySet<(NotificationType Type, NotificationChannel Channel)> Enabled)
{
    public bool IsEnabled(NotificationType type, NotificationChannel channel)
        => Enabled.Contains((type, channel));

    // Defaults for a user who has never customized: in-app on for every type, email/push
    // off (no providers yet).
    public static NotificationPreferences Default(string userId)
        => new(userId, new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.MiniLeagueUpdate, NotificationChannel.InApp),
            (NotificationType.RoundResult, NotificationChannel.InApp),
            (NotificationType.SystemMessage, NotificationChannel.InApp),
        });
}
