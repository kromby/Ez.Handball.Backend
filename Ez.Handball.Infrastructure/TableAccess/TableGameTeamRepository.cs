using Azure;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameTeamRepository : IGameTeamRepository
{
    private readonly TableServiceClient _client;

    public TableGameTeamRepository(TableServiceClient client)
    {
        _client = client;
    }

    public async Task<bool> ExistsAsync(string userId, GameFlavor flavor, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeams);
        try
        {
            await table.GetEntityAsync<GameTeamEntity>(userId, Flavor(flavor), cancellationToken: ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task CreateAsync(
        string userId, GameFlavor flavor, string name, DateTimeOffset createdAt, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeams);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new GameTeamEntity
        {
            PartitionKey = userId,
            RowKey = Flavor(flavor),
            TeamId = GameTeamId.For(userId, flavor),
            Name = name,
            CreatedAt = createdAt
        }, TableUpdateMode.Replace, ct);
    }

    private static string Flavor(GameFlavor flavor) => flavor.ToString().ToLowerInvariant();
}
