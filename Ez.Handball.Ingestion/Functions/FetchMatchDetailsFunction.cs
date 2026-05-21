using System.Text.Json;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public class FetchMatchDetailsFunction
{
    private readonly IHsiApiClient _apiClient;
    private readonly IBlobArchiver _blobArchiver;

    public FetchMatchDetailsFunction(IHsiApiClient apiClient, IBlobArchiver blobArchiver)
    {
        _apiClient = apiClient;
        _blobArchiver = blobArchiver;
    }

    [Function("FetchMatchDetails")]
    public async Task RunAsync(
        [BlobTrigger("raw/tournaments/{tournamentId}/matches.json", Connection = "HandballStorageConnection")] string blobContent,
        string tournamentId,
        FunctionContext context)
    {
        var logger = context.GetLogger<FetchMatchDetailsFunction>();
        await ProcessAsync(blobContent, logger);
    }

    public async Task ProcessAsync(string blobContent, ILogger? logger = null)
    {
        var response = JsonSerializer.Deserialize<MatchListResponse>(blobContent)
            ?? new MatchListResponse();

        foreach (var match in response.Data)
        {
            try
            {
                var detailsPath = $"matches/{match.GameId}/details.json";
                var isFinished = match.Status == "S";
                var blobExists = await _blobArchiver.ExistsAsync(detailsPath);

                if (isFinished && blobExists)
                {
                    logger?.LogInformation("Skipping finished match {MatchId} — details already archived", match.GameId);
                    continue;
                }

                var detailsJson = await _apiClient.GetMatchDetailsJsonAsync(match.GameId);
                var homePlayersJson = await _apiClient.GetMatchPlayerStatsJsonAsync(match.GameId, match.HomeTeamId);
                var awayPlayersJson = await _apiClient.GetMatchPlayerStatsJsonAsync(match.GameId, match.AwayTeamId);

                await _blobArchiver.SaveAsync(detailsPath, detailsJson);
                await _blobArchiver.SaveAsync($"matches/{match.GameId}/players-{match.HomeTeamId}.json", homePlayersJson);
                await _blobArchiver.SaveAsync($"matches/{match.GameId}/players-{match.AwayTeamId}.json", awayPlayersJson);

                logger?.LogInformation("Archived details and player stats for match {MatchId}", match.GameId);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to process match {MatchId}", match.GameId);
            }
        }
    }
}
