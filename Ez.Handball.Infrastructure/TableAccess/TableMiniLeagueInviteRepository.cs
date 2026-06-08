using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableMiniLeagueInviteRepository : IMiniLeagueInviteRepository
{
    private const string Partition = "invite";

    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableMiniLeagueInviteRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task AddAsync(MiniLeagueInvite invite, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MiniLeagueInvites);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new MiniLeagueInviteEntity
        {
            PartitionKey = Partition,
            RowKey = invite.Token,
            LeagueId = invite.LeagueId,
            CreatedByUserId = invite.CreatedByUserId,
            CreatedAt = invite.CreatedAt,
            ExpiresAt = invite.ExpiresAt
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<MiniLeagueInvite?> GetByTokenAsync(string token, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MiniLeagueInvites);
        try
        {
            var e = (await table.GetEntityAsync<MiniLeagueInviteEntity>(Partition, token, cancellationToken: ct)).Value;
            return Map(e);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null; // row or table missing
        }
    }

    public async Task<MiniLeagueInvite?> GetByLeagueAsync(string leagueId, CancellationToken ct)
    {
        await foreach (var e in _query.QueryAsync<MiniLeagueInviteEntity>(
                           Tables.MiniLeagueInvites,
                           $"PartitionKey eq '{Partition}' and LeagueId eq '{ODataFilter.Escape(leagueId)}'", ct))
        {
            return Map(e); // 0 or 1 active invite per league
        }
        return null;
    }

    public async Task DeleteByTokenAsync(string token, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MiniLeagueInvites);
        try
        {
            await table.DeleteEntityAsync(Partition, token, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // idempotent: row or table already gone
        }
    }

    private static MiniLeagueInvite Map(MiniLeagueInviteEntity e) =>
        new(e.RowKey, e.LeagueId, e.CreatedByUserId, e.CreatedAt, e.ExpiresAt);
}
