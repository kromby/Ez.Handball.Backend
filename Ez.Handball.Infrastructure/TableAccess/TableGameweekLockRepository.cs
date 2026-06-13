using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekLockRepository : IGameweekLockRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableGameweekLockRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task<DateTimeOffset?> GetPinnedDeadlineAsync(
        string tournamentId, string roundLabel, CancellationToken ct)
    {
        var filter = $"PartitionKey eq '{ODataFilter.Escape(tournamentId)}' and RowKey eq '{ODataFilter.Escape(roundLabel)}'";
        await foreach (var e in _query.QueryAsync<GameweekLockEntity>(Tables.GameweekLocks, filter, ct))
            return e.PinnedDeadline;
        return null;
    }

    public async Task PinAsync(
        string tournamentId, string roundLabel, DateTimeOffset deadline, DateTimeOffset lockedAt, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameweekLocks);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        // First-write-wins: only pin if no row exists yet, so a passed deadline can't shift later.
        var existing = await GetPinnedDeadlineAsync(tournamentId, roundLabel, ct);
        if (existing is not null) return;

        await table.UpsertEntityAsync(new GameweekLockEntity
        {
            PartitionKey = tournamentId,
            RowKey = roundLabel,
            PinnedDeadline = deadline,
            LockedAt = lockedAt
        }, TableUpdateMode.Replace, ct);
    }
}
