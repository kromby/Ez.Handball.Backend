using Azure;
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

        // Atomic first-write-wins: AddEntity fails with 409 if the row already exists, so a passed
        // deadline can never be overwritten — even under a concurrent pin of the same gameweek.
        try
        {
            await table.AddEntityAsync(new GameweekLockEntity
            {
                PartitionKey = tournamentId,
                RowKey = roundLabel,
                PinnedDeadline = deadline,
                LockedAt = lockedAt
            }, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already pinned by an earlier/concurrent write — first write wins, nothing to do.
        }
    }
}
