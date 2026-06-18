using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

// One row per placed player, all under the team's partition key. A save fully replaces the
// set via a single-partition transaction (a 15-player squad is well under the 100-action cap).
internal sealed class TableLineupRepository : ILineupRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableLineupRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<Lineup?> GetAsync(string teamId, CancellationToken ct)
    {
        var slots = new List<LineupSlot>();
        await foreach (var e in _query.QueryAsync<GameLineupEntity>(
                           Tables.GameLineups, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
        {
            if (Enum.TryParse<LineupRole>(e.Role, out var role)) // tolerate unknown stored roles by skipping
                slots.Add(new LineupSlot(e.RowKey, role, e.BenchOrder));
        }
        return slots.Count == 0 ? null : new Lineup(slots);
    }

    public async Task ReplaceAsync(string teamId, Lineup lineup, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameLineups);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var desired = lineup.Slots.ToDictionary(s => s.PlayerId);
        var actions = new List<TableTransactionAction>();

        // Delete rows no longer present.
        await foreach (var e in _query.QueryAsync<GameLineupEntity>(
                           Tables.GameLineups, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
        {
            if (!desired.ContainsKey(e.RowKey))
                actions.Add(new TableTransactionAction(TableTransactionActionType.Delete,
                    new GameLineupEntity { PartitionKey = teamId, RowKey = e.RowKey, ETag = ETag.All }));
        }

        // Upsert every desired slot.
        foreach (var s in lineup.Slots)
            actions.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace,
                new GameLineupEntity
                {
                    PartitionKey = teamId,
                    RowKey = s.PlayerId,
                    Role = s.Role.ToString(),
                    BenchOrder = s.BenchOrder
                }));

        if (actions.Count > 0)
            await table.SubmitTransactionAsync(actions, ct);
    }

    // Full-partition scan to collect the distinct team ids. This is intentional: it backs the
    // debug-only admin settle fan-out (#96), which runs over the handful of registered teams in a
    // replay session, not a hot read path — a dedicated team-id index would be unwarranted machinery.
    public async Task<IReadOnlyList<string>> ListTeamIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var e in _query.QueryAsync<GameLineupEntity>(Tables.GameLineups, null, ct))
            ids.Add(e.PartitionKey);
        return ids.ToList();
    }
}
