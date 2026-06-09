using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableNotificationPreferenceRepository : INotificationPreferenceRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableNotificationPreferenceRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<NotificationPreferences?> GetAsync(string userId, CancellationToken ct)
    {
        var cells = new HashSet<(NotificationType, NotificationChannel)>();
        var any = false;
        await foreach (var e in _query.QueryAsync<NotificationPreferenceEntity>(
                           Tables.NotificationPreferences,
                           $"PartitionKey eq '{ODataFilter.Escape(userId)}'", ct))
        {
            any = true; // a row exists, so the user has configured preferences
            if (Enum.TryParse<NotificationType>(e.Type, out var type)
                && Enum.TryParse<NotificationChannel>(e.Channel, out var channel))
            {
                cells.Add((type, channel)); // silently drop rows with unknown enum names
            }
        }

        return any ? new NotificationPreferences(userId, cells) : null;
    }

    public async Task UpsertAsync(NotificationPreferences preferences, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.NotificationPreferences);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var desired = preferences.Enabled
            .ToDictionary(c => RowKeyFor(c.Type, c.Channel), c => c);

        // Remove cells that are no longer enabled.
        await foreach (var e in _query.QueryAsync<NotificationPreferenceEntity>(
                           Tables.NotificationPreferences,
                           $"PartitionKey eq '{ODataFilter.Escape(preferences.UserId)}'", ct))
        {
            if (!desired.ContainsKey(e.RowKey))
            {
                await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All, ct);
            }
        }

        // Upsert every enabled cell.
        foreach (var (rowKey, cell) in desired)
        {
            await table.UpsertEntityAsync(new NotificationPreferenceEntity
            {
                PartitionKey = preferences.UserId,
                RowKey = rowKey,
                Type = cell.Type.ToString(),
                Channel = cell.Channel.ToString()
            }, TableUpdateMode.Replace, ct);
        }
    }

    private static string RowKeyFor(NotificationType type, NotificationChannel channel)
        => $"{type}:{channel}";
}
