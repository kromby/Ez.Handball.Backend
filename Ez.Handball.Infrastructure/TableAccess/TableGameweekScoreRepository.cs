using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableGameweekScoreRepository : IGameweekScoreRepository
{
    private readonly TableServiceClient _client;
    private readonly ITableQuery _query;

    public TableGameweekScoreRepository(TableServiceClient client, ITableQuery query)
    {
        _client = client;
        _query = query;
    }

    public async Task SaveAsync(GameweekScore score, CancellationToken ct)
    {
        var table = _client.GetTableClient(Tables.GameweekScores);
        await table.CreateIfNotExistsAsync(cancellationToken: ct);

        await table.UpsertEntityAsync(new GameweekScoreEntity
        {
            PartitionKey = score.TeamId,
            RowKey = score.RoundLabel,
            Points = score.Points,
            CaptainPlayerId = score.CaptainPlayerId,
            BreakdownJson = JsonSerializer.Serialize(score.Breakdown)
        }, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<GameweekScore>> ListByTeamAsync(string teamId, CancellationToken ct)
    {
        var result = new List<GameweekScore>();
        await foreach (var e in _query.QueryAsync<GameweekScoreEntity>(
                           Tables.GameweekScores, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
        {
            var breakdown = string.IsNullOrWhiteSpace(e.BreakdownJson)
                ? Array.Empty<GameweekPlayerScore>()
                : JsonSerializer.Deserialize<GameweekPlayerScore[]>(e.BreakdownJson) ?? Array.Empty<GameweekPlayerScore>();
            result.Add(new GameweekScore(e.PartitionKey, e.RowKey, e.Points, e.CaptainPlayerId, breakdown));
        }
        return result;
    }
}
