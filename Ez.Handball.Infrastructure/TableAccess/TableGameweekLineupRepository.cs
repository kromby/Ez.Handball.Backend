using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekLineupRepository : IGameweekLineupRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableGameweekLineupRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    private static string Partition(string teamId, string roundLabel) => $"{teamId}|{roundLabel}";

    public async Task<Lineup?> GetSnapshotAsync(string teamId, string roundLabel, CancellationToken ct)
    {
        var pk = Partition(teamId, roundLabel);
        var slots = new List<LineupSlot>();
        await foreach (var e in _query.QueryAsync<GameweekLineupEntity>(
                           Tables.GameweekLineups, $"PartitionKey eq '{ODataFilter.Escape(pk)}'", ct))
        {
            if (Enum.TryParse<LineupRole>(e.Role, out var role))
                slots.Add(new LineupSlot(e.RowKey, role, e.BenchOrder));
        }
        return slots.Count == 0 ? null : new Lineup(slots);
    }

    public async Task SaveSnapshotAsync(string teamId, string roundLabel, Lineup lineup, CancellationToken ct)
    {
        var pk = Partition(teamId, roundLabel);
        var table = _client.GetTableClient(Tables.GameweekLineups);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var desired = lineup.Slots.Select(s => s.PlayerId).ToHashSet();
        var actions = new List<TableTransactionAction>();

        // Full replacement: delete any prior snapshot rows whose player is no longer present, so a
        // re-save (e.g. a player swapped out) leaves no orphans — the save is idempotent.
        await foreach (var e in _query.QueryAsync<GameweekLineupEntity>(
                           Tables.GameweekLineups, $"PartitionKey eq '{ODataFilter.Escape(pk)}'", ct))
        {
            if (!desired.Contains(e.RowKey))
                actions.Add(new TableTransactionAction(TableTransactionActionType.Delete,
                    new GameweekLineupEntity { PartitionKey = pk, RowKey = e.RowKey, ETag = ETag.All }));
        }

        foreach (var s in lineup.Slots)
            actions.Add(new TableTransactionAction(
                TableTransactionActionType.UpsertReplace,
                new GameweekLineupEntity
                {
                    PartitionKey = pk,
                    RowKey = s.PlayerId,
                    Role = s.Role.ToString(),
                    BenchOrder = s.BenchOrder
                }));

        if (actions.Count > 0)
            await table.SubmitTransactionAsync(actions, ct);
    }
}
