using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableMiniLeagueRepository : IMiniLeagueRepository
{
    private const string HeaderPartition = "league";

    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableMiniLeagueRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task CreateAsync(MiniLeague league, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MiniLeagues);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new MiniLeagueEntity
        {
            PartitionKey = HeaderPartition,
            RowKey = league.Id,
            Name = league.Name,
            Season = league.Season,
            CreatorUserId = league.CreatorUserId,
            CreatedAt = league.CreatedAt
        }, TableUpdateMode.Replace, ct);
    }

    public async Task AddMemberAsync(string leagueId, MiniLeagueMember member, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MiniLeagueMembers);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new MiniLeagueMemberEntity
        {
            PartitionKey = leagueId,
            RowKey = member.UserId,
            Role = member.Role,
            JoinedAt = member.JoinedAt
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<MiniLeague?> GetAsync(string leagueId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.MiniLeagues);
        try
        {
            var e = (await table.GetEntityAsync<MiniLeagueEntity>(HeaderPartition, leagueId, cancellationToken: ct)).Value;
            return new MiniLeague(e.RowKey, e.Name, e.Season, e.CreatorUserId, e.CreatedAt);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null; // row or table missing
        }
    }

    public async Task<IReadOnlyList<MiniLeagueMember>> GetMembersAsync(string leagueId, CancellationToken ct)
    {
        var members = new List<MiniLeagueMember>();
        await foreach (var e in _query.QueryAsync<MiniLeagueMemberEntity>(
                           Tables.MiniLeagueMembers, $"PartitionKey eq '{ODataFilter.Escape(leagueId)}'", ct))
        {
            members.Add(new MiniLeagueMember(e.RowKey, e.Role, e.JoinedAt));
        }
        return members;
    }
}
