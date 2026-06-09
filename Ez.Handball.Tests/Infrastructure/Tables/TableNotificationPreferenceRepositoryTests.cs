using Azure.Data.Tables;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;

namespace Ez.Handball.Tests.Infrastructure.Tables;

using Tables = Ez.Handball.Infrastructure.Tables;

[Collection("Azurite")]
public class TableNotificationPreferenceRepositoryTests : IAsyncLifetime
{
    private readonly TableServiceClient _client = new("UseDevelopmentStorage=true");
    private readonly ITableQuery _query;

    public TableNotificationPreferenceRepositoryTests() => _query = new TableQuery(_client);

    private TableNotificationPreferenceRepository Sut() => new(_client, _query);

    public async Task InitializeAsync()
        => await _client.GetTableClient(Tables.NotificationPreferences).CreateIfNotExistsAsync();

    public async Task DisposeAsync()
        => await _client.GetTableClient(Tables.NotificationPreferences).DeleteAsync();

    [Fact]
    public async Task Get_ReturnsNull_WhenNoRowsStored()
    {
        var got = await Sut().GetAsync("u-none", default);

        Assert.Null(got);
    }

    [Fact]
    public async Task Upsert_ThenGet_RoundTripsEnabledCells()
    {
        var prefs = new NotificationPreferences("u-1", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
            (NotificationType.MiniLeagueUpdate, NotificationChannel.InApp),
        });

        await Sut().UpsertAsync(prefs, default);
        var got = await Sut().GetAsync("u-1", default);

        Assert.NotNull(got);
        Assert.True(got!.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.True(got.IsEnabled(NotificationType.MiniLeagueUpdate, NotificationChannel.InApp));
        Assert.False(got.IsEnabled(NotificationType.RoundResult, NotificationChannel.InApp));
    }

    [Fact]
    public async Task Upsert_RemovesCells_NoLongerEnabled()
    {
        await Sut().UpsertAsync(new NotificationPreferences("u-2", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
            (NotificationType.RoundResult, NotificationChannel.Push),
        }), default);

        // Re-save with Push removed.
        await Sut().UpsertAsync(new NotificationPreferences("u-2", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
        }), default);

        var got = await Sut().GetAsync("u-2", default);

        Assert.NotNull(got);
        Assert.True(got!.IsEnabled(NotificationType.RoundResult, NotificationChannel.Email));
        Assert.False(got.IsEnabled(NotificationType.RoundResult, NotificationChannel.Push));
    }

    [Fact]
    public async Task Upsert_EmptySet_RemovesAllRows_AndGetReturnsNull()
    {
        await Sut().UpsertAsync(new NotificationPreferences("u-3", new HashSet<(NotificationType, NotificationChannel)>
        {
            (NotificationType.RoundResult, NotificationChannel.Email),
        }), default);

        // User disables everything.
        await Sut().UpsertAsync(
            new NotificationPreferences("u-3", new HashSet<(NotificationType, NotificationChannel)>()),
            default);

        var got = await Sut().GetAsync("u-3", default);

        Assert.Null(got); // no rows left → "never configured" contract
    }
}
