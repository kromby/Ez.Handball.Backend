using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameBudgetRepository : IGameBudgetRepository
{
    private const string BalanceRow = "balance";
    private const int MaxRetries = 5;

    private readonly TableServiceClient _client;

    public TableGameBudgetRepository(TableServiceClient client) => _client = client;

    public async Task<double?> GetBalanceAsync(string teamId, CancellationToken ct)
    {
        try
        {
            var e = (await _client.GetTableClient(Tables.GameBudgets)
                .GetEntityAsync<GameBudgetEntity>(teamId, BalanceRow, cancellationToken: ct)).Value;
            return e.Amount;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task CreateAsync(string teamId, double amount, DateTimeOffset now, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameBudgets);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new GameBudgetEntity
        {
            PartitionKey = teamId, RowKey = BalanceRow, Amount = amount, UpdatedAt = now
        }, TableUpdateMode.Replace, ct);
    }

    public Task<bool> TryDeductAsync(string teamId, double amount, DateTimeOffset now, CancellationToken ct)
        => AdjustAsync(teamId, -amount, requireNonNegative: true, now, ct);

    public Task<bool> TryCreditAsync(string teamId, double amount, DateTimeOffset now, CancellationToken ct)
        => AdjustAsync(teamId, amount, requireNonNegative: false, now, ct);

    private async Task<bool> AdjustAsync(
        string teamId, double delta, bool requireNonNegative, DateTimeOffset now, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameBudgets);
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            GameBudgetEntity e;
            try
            {
                e = (await table.GetEntityAsync<GameBudgetEntity>(teamId, BalanceRow, cancellationToken: ct)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }

            var next = e.Amount + delta;
            if (requireNonNegative && next < 0) return false;

            e.Amount = next;
            e.UpdatedAt = now;
            try
            {
                await table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // lost the optimistic-concurrency race — re-read and retry
            }
        }
        return false;
    }
}
