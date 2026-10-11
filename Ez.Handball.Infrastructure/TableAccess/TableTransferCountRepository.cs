using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableTransferCountRepository : ITransferCountRepository
{
    private const int MaxRetries = 5;

    private readonly TableServiceClient _client;

    public TableTransferCountRepository(TableServiceClient client) => _client = client;

    public async Task IncrementAsync(
        string teamId, string roundLabel, TransferType type, DateTimeOffset now, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTransferCounts);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        // Optimistic read-modify-write, same idiom as TableGameBudgetRepository: a lost race
        // (412 on update, 409 on a concurrent first insert) re-reads and retries.
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var existing = await table.GetEntityIfExistsAsync<GameTransferCountEntity>(
                    teamId, roundLabel, cancellationToken: ct);
                if (!existing.HasValue)
                {
                    var created = new GameTransferCountEntity { PartitionKey = teamId, RowKey = roundLabel, UpdatedAt = now };
                    Add(created, type);
                    await table.AddEntityAsync(created, ct);
                    return;
                }

                var e = existing.Value!;
                Add(e, type);
                e.UpdatedAt = now;
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // lost the race — re-read and retry
            }
        }

        throw new InvalidOperationException(
            $"Could not record a {type} for team {teamId} round {roundLabel} after {MaxRetries} attempts.");
    }

    public async Task<IReadOnlyList<RoundTransferCount>> ListAsync(string teamId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTransferCounts);
        var rows = new List<RoundTransferCount>();
        try
        {
            await foreach (var e in table.QueryAsync<GameTransferCountEntity>(
                               x => x.PartitionKey == teamId, cancellationToken: ct))
                rows.Add(new RoundTransferCount(e.RowKey, e.Buys, e.Sells));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // table not created yet — nobody has transferred
        }
        return rows;
    }

    private static void Add(GameTransferCountEntity e, TransferType type)
    {
        if (type == TransferType.Buy) e.Buys++;
        else e.Sells++;
    }
}
