using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly TableServiceClient _client;

    public TableRefreshTokenRepository(TableServiceClient client) => _client = client;

    public async Task AddAsync(RefreshTokenEntity token, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.RefreshTokens);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.AddEntityAsync(token, ct);
    }

    public async Task<RefreshTokenEntity?> GetAsync(string userId, string secretHash, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.RefreshTokens);
        try
        {
            return (await table.GetEntityAsync<RefreshTokenEntity>(userId, secretHash, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string userId, string secretHash, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.RefreshTokens);
        await table.DeleteEntityAsync(userId, secretHash, ETag.All, ct); // 404 swallowed by SDK
    }

    public async Task DeleteAllForUserAsync(string userId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.RefreshTokens);
        try
        {
            await foreach (var row in table.QueryAsync<RefreshTokenEntity>(
                               r => r.PartitionKey == userId, cancellationToken: ct))
            {
                await table.DeleteEntityAsync(row.PartitionKey, row.RowKey, ETag.All, ct);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // table doesn't exist → nothing to revoke
        }
    }
}
