using System.Text.Json;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public class ParsePlayersFunction
{
    private readonly ITableWriter _tableWriter;

    public ParsePlayersFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("ParsePlayers")]
    public async Task RunAsync(
        [BlobTrigger("raw/matches/{matchId}/players-{clubId}.json", Connection = "AzureWebJobsStorage")] string blobContent,
        string matchId,
        string clubId,
        FunctionContext context)
    {
        var logger = context.GetLogger<ParsePlayersFunction>();
        await ProcessAsync(blobContent, matchId, clubId, logger);
    }

    public async Task ProcessAsync(string blobContent, string matchId, string clubId, ILogger? logger = null)
    {
        var response = JsonSerializer.Deserialize<PlayerStatsResponse>(blobContent);
        var players = response?.Data;

        if (players is null)
        {
            logger?.LogError("Failed to deserialize player stats for matchId {MatchId}, clubId {ClubId}", matchId, clubId);
            return;
        }

        var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'");

        if (matches.Count == 0)
        {
            logger?.LogError(
                "Match {MatchId} not found in table — skipping player stats for clubId {ClubId}",
                matchId, clubId);
            return;
        }

        var match = matches[0];
        var teamId = match.HomeTeamId.StartsWith($"{clubId}-")
            ? match.HomeTeamId
            : match.AwayTeamId;

        foreach (var player in players.Where(p => p.Player == "1"))
        {
            var playerId = player.PlayerId;

            _ = int.TryParse(player.Goals, out var goals);
            _ = int.TryParse(player.YellowCards, out var yellowCards);
            _ = int.TryParse(player.RedCards, out var redCards);

            await _tableWriter.UpsertAsync("Players", new PlayerEntity
            {
                PartitionKey = teamId,
                RowKey = playerId,
                Name = player.Name,
                Position = player.Position
            });

            await _tableWriter.UpsertAsync("PlayerStats", new PlayerStatEntity
            {
                PartitionKey = matchId,
                RowKey = playerId,
                Goals = goals,
                YellowCards = yellowCards,
                RedCards = redCards,
                MinutesPlayed = 0
            });
        }

        logger?.LogInformation(
            "Parsed player stats for match {MatchId}, club {ClubId}, team {TeamId}",
            matchId, clubId, teamId);
    }
}
