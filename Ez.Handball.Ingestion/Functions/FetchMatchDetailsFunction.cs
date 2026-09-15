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

                if (isFinished && await ArchivedDetailsAreFinishedAsync(detailsPath))
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

    // hsi.is archives a placeholder details report (REPORT_STATUS "1", no scores) as soon as a
    // match is fetched — even long before it's played. Once the match list later flips that
    // match to "S", the existence of A details blob doesn't mean it's the FINISHED report: it
    // may still be that pre-match placeholder. Only the blob's own REPORT_STATUS can tell.
    private async Task<bool> ArchivedDetailsAreFinishedAsync(string detailsPath)
    {
        if (!await _blobArchiver.ExistsAsync(detailsPath)) return false;

        try
        {
            var existing = await _blobArchiver.ReadAsync(detailsPath);
            var details = JsonSerializer.Deserialize<MatchDetailsResponse>(existing);
            return details?.Data?.ReportStatus == "S";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
