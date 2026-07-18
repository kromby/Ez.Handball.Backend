using System.Text.Json;
using Ez.Handball.Ingestion.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public class FetchHbStatzMatchStatsFunction
{
    private readonly IMatchReportClient _reportClient;
    private readonly IBlobArchiver _blobArchiver;

    public FetchHbStatzMatchStatsFunction(IMatchReportClient reportClient, IBlobArchiver blobArchiver)
    {
        _reportClient = reportClient;
        _blobArchiver = blobArchiver;
    }

    [Function("FetchHbStatzMatchStats")]
    public async Task RunAsync(
        [QueueTrigger("hbstatz-match-sync", Connection = "HandballStorageConnection")] string message,
        FunctionContext context)
    {
        var logger = context.GetLogger<FetchHbStatzMatchStatsFunction>();
        var doc = JsonSerializer.Deserialize<JsonElement>(message);
        var matchId = doc.GetProperty("MatchId").GetString() ?? string.Empty;

        logger.LogInformation("Scraping HBStatz team stats for match {MatchId}", matchId);

        var homeHtml = await _reportClient.GetTeamPageHtmlAsync(matchId, "home", context.CancellationToken);
        var awayHtml = await _reportClient.GetTeamPageHtmlAsync(matchId, "away", context.CancellationToken);

        await _blobArchiver.SaveAsync($"hbstatz/matches/{matchId}/players-home.html", homeHtml, context.CancellationToken);
        await _blobArchiver.SaveAsync($"hbstatz/matches/{matchId}/players-away.html", awayHtml, context.CancellationToken);

        logger.LogInformation("Archived HBStatz team stats for match {MatchId}", matchId);
    }
}
