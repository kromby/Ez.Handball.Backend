using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class NotificationPreferencesTests
{
    [Fact]
    public void IsEnabled_DistinguishesEnabledAndDisabledCells()
    {
        var prefs = new NotificationPreferences("u-1", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
        });

        Assert.True(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.False(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.Push));
    }

    [Fact]
    public void Default_EnablesInAppForAllTypes_AndNothingElse()
    {
        var prefs = NotificationPreferences.Default("u-1");

        Assert.True(prefs.IsEnabled(NotificationType.MiniLeagueUpdate, NotificationChannel.InApp));
        Assert.True(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.InApp));
        Assert.True(prefs.IsEnabled(NotificationType.SystemMessage, NotificationChannel.InApp));
        Assert.False(prefs.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.False(prefs.IsEnabled(NotificationType.MiniLeagueUpdate, NotificationChannel.Push));
    }
}
