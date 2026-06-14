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
        => await ReadAsync(userId, flavor, ct) is not null;

    public async Task CreateAsync(
        string userId, GameFlavor flavor, string name, string color, DateTimeOffset createdAt, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeams);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(new GameTeamEntity
        {
            PartitionKey = userId,
            RowKey = Flavor(flavor),
            TeamId = GameTeamId.For(userId, flavor),
            Name = name,
            Color = color,
            CreatedAt = createdAt
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<GameTeam?> GetAsync(string userId, GameFlavor flavor, CancellationToken ct)
    {
        var row = await ReadAsync(userId, flavor, ct);
        return row is null ? null : new GameTeam(row.TeamId, row.Name, row.Color, row.CreatedAt);
    }

    public async Task RenameAsync(string userId, GameFlavor flavor, string newName, CancellationToken ct)
    {
        var row = await ReadAsync(userId, flavor, ct)
            ?? throw new InvalidOperationException($"No team for {userId}:{Flavor(flavor)}");
        row.Name = newName;
        var table = _client.GetTableClient(Tables.GameTeams);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);
        await table.UpsertEntityAsync(row, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<GameTeam>> ListByFlavorAsync(GameFlavor flavor, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeams);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        var result = new List<GameTeam>();
        await foreach (var e in table.QueryAsync<GameTeamEntity>(
                           filter: $"RowKey eq '{Flavor(flavor)}'", cancellationToken: ct))
        {
            result.Add(new GameTeam(e.TeamId, e.Name, e.Color, e.CreatedAt));
        }
        return result;
    }

    private async Task<GameTeamEntity?> ReadAsync(string userId, GameFlavor flavor, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameTeams);
        try
        {
            return (await table.GetEntityAsync<GameTeamEntity>(userId, Flavor(flavor), cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static string Flavor(GameFlavor flavor) => flavor.ToString().ToLowerInvariant();
}
