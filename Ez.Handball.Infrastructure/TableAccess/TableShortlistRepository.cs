using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableShortlistRepository : IShortlistRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableShortlistRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<ShortlistEntry?> GetAsync(string userId, string playerId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.Shortlists);
        try
        {
            var e = (await table.GetEntityAsync<ShortlistEntryEntity>(userId, playerId, cancellationToken: ct)).Value;
            return new ShortlistEntry(e.RowKey, e.CreatedAt, e.DeletedAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null; // row or table missing
        }
    }

    public async Task<int> CountActiveAsync(string userId, CancellationToken ct)
    {
        var count = 0;
        await foreach (var e in _query.QueryAsync<ShortlistEntryEntity>(
                           Tables.Shortlists, $"PartitionKey eq '{ODataFilter.Escape(userId)}'", ct))
        {
            if (e.DeletedAt is null) count++; // null filtered in memory — Table Storage omits null props
        }
        return count;
    }

    public async Task UpsertAsync(
        string userId, string playerId, DateTimeOffset createdAt, DateTimeOffset? deletedAt, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.Shortlists);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new ShortlistEntryEntity
        {
            PartitionKey = userId,
            RowKey = playerId,
            CreatedAt = createdAt,
            DeletedAt = deletedAt
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<ShortlistEntry>> ListActiveAsync(string userId, CancellationToken ct)
    {
        var entries = new List<ShortlistEntry>();
        await foreach (var e in _query.QueryAsync<ShortlistEntryEntity>(
                           Tables.Shortlists, $"PartitionKey eq '{ODataFilter.Escape(userId)}'", ct))
        {
            if (e.DeletedAt is null)
                entries.Add(new ShortlistEntry(e.RowKey, e.CreatedAt, e.DeletedAt));
        }
        return entries;
    }
}
