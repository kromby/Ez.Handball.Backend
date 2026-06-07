using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameRosterRepository : IGameRosterRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableGameRosterRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<IReadOnlyList<RosterEntry>> ListActiveAsync(string teamId, CancellationToken ct)
    {
        var entries = new List<RosterEntry>();
        await foreach (var e in _query.QueryAsync<GameRosterEntity>(
                           Tables.GameRosters, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
        {
            if (e.DeletedAt is null)
                entries.Add(new RosterEntry(e.RowKey, e.Position, e.PricePaidAmount, e.DeletedAt));
        }
        return entries;
    }

    public async Task<RosterEntry?> GetAsync(string teamId, string playerId, CancellationToken ct)
    {
        try
        {
            var e = (await _client.GetTableClient(Tables.GameRosters)
                .GetEntityAsync<GameRosterEntity>(teamId, playerId, cancellationToken: ct)).Value;
            return new RosterEntry(e.RowKey, e.Position, e.PricePaidAmount, e.DeletedAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<RosterAddOutcome> AddOrResurrectAsync(
        string teamId, string playerId, string? position, double pricePaid, DateTimeOffset now, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameRosters);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var entity = new GameRosterEntity
        {
            PartitionKey = teamId, RowKey = playerId, Position = position,
            PricePaidAmount = pricePaid, CreatedAt = now, DeletedAt = null
        };

        try
        {
            await table.AddEntityAsync(entity, ct);
            return RosterAddOutcome.Added;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var existing = (await table.GetEntityAsync<GameRosterEntity>(teamId, playerId, cancellationToken: ct)).Value;
            if (existing.DeletedAt is null) return RosterAddOutcome.AlreadyActive;

            existing.Position = position;
            existing.PricePaidAmount = pricePaid;
            existing.CreatedAt = now;
            existing.DeletedAt = null;
            try
            {
                await table.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct);
                return RosterAddOutcome.Added;
            }
            catch (RequestFailedException ex2) when (ex2.Status == 412)
            {
                return RosterAddOutcome.AlreadyActive; // raced with a concurrent buy
            }
        }
    }

    public async Task SoftDeleteAsync(string teamId, string playerId, DateTimeOffset now, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameRosters);
        try
        {
            var e = (await table.GetEntityAsync<GameRosterEntity>(teamId, playerId, cancellationToken: ct)).Value;
            e.DeletedAt = now;
            await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // nothing to delete
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            // ETag mismatch: a concurrent writer already updated or soft-deleted the row.
            // Soft-delete is idempotent — the row is already gone from the active set.
        }
    }
}
