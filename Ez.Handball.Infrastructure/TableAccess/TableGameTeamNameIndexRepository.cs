using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameTeamNameIndexRepository : IGameTeamNameIndexRepository
{
    private readonly TableServiceClient _client;

    public TableGameTeamNameIndexRepository(TableServiceClient client) => _client = client;

    public async Task<bool> TryReserveAsync(string normalizedName, string teamId, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeamNameIndex);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        try
        {
            await table.AddEntityAsync(
                new GameTeamNameIndexEntity { RowKey = normalizedName, TeamId = teamId }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false; // name already indexed → uniqueness violation
        }
    }

    public async Task ReleaseAsync(string normalizedName, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeamNameIndex);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.DeleteEntityAsync("name", normalizedName, ETag.All, ct); // 404 swallowed by SDK
    }
}
