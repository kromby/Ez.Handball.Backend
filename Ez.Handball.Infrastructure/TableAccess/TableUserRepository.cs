using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableUserRepository : IUserRepository
{
    private readonly TableServiceClient _client;

    public TableUserRepository(TableServiceClient client) => _client = client;

    public Task<UserEntity?> GetByIdAsync(string userId, CancellationToken ct)
        => GetAsync<UserEntity>(Tables.Users, "user", userId, ct);

    public async Task<UserEntity?> GetByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        var index = await GetAsync<UserEmailIndexEntity>(Tables.UserEmailIndex, "email", normalizedEmail, ct);
        return index is null ? null : await GetByIdAsync(index.UserId, ct);
    }

    public async Task<bool> TryReserveEmailAsync(string normalizedEmail, string userId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.UserEmailIndex);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        try
        {
            await table.AddEntityAsync(
                new UserEmailIndexEntity { RowKey = normalizedEmail, UserId = userId }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false; // email already indexed → uniqueness violation
        }
    }

    public async Task AddAsync(UserEntity user, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.Users);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.AddEntityAsync(user, ct);
    }

    public async Task UpdateAsync(UserEntity user, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.Users);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(user, TableUpdateMode.Replace, ct);
    }

    private async Task<T?> GetAsync<T>(string tableName, string pk, string rk, CancellationToken ct)
        where T : class, ITableEntity, new()
    {
        var table = _client.GetTableClient(tableName);
        try
        {
            return (await table.GetEntityAsync<T>(pk, rk, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
