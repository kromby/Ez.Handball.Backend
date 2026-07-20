using System.Net;
using System.Text.Json;
using Azure.Storage.Queues;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public class TriggerHbStatzSyncFunction
{
    private readonly ITableWriter _tableWriter;
    private readonly QueueServiceClient _queueServiceClient;

    public TriggerHbStatzSyncFunction(ITableWriter tableWriter, QueueServiceClient queueServiceClient)
    {
        _tableWriter = tableWriter;
        _queueServiceClient = queueServiceClient;
    }

    [Function("TriggerHbStatzSync")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/sync/hbstatz")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<TriggerHbStatzSyncFunction>();
        var season = req.Query["season"];
        var limitStr = req.Query["limit"];
        int? limit = int.TryParse(limitStr, out var l) ? l : null;

        logger.LogInformation("Manually triggering HBStatz sync. Season filter: {Season}, Limit: {Limit}", season ?? "None", limit?.ToString() ?? "None");

        var enqueuedCount = await ProcessAsync(season, limit, context.CancellationToken);

        logger.LogInformation("Finished manual HBStatz trigger. Enqueued {Count} matches", enqueuedCount);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            message = $"Successfully enqueued {enqueuedCount} finished games for HBStatz scraping/ingestion.",
            enqueuedCount
        });

        return response;
    }

    public async Task<int> ProcessAsync(string? season, int? limit, CancellationToken ct)
    {
        var filter = "IngestHbStatz eq true";
        if (!string.IsNullOrWhiteSpace(season))
        {
            filter += $" and PartitionKey eq '{ODataFilter.Escape(season)}'";
        }

        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>("Tournaments", filter, ct);
        var queueClient = _queueServiceClient.GetQueueClient("hbstatz-match-sync");
        await queueClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var enqueuedCount = 0;

        foreach (var tournament in tournaments)
        {
            var tournamentId = tournament.RowKey;
            
            var matchFilter = $"PartitionKey eq '{ODataFilter.Escape(tournamentId)}' and Status eq 'S'";
            var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", matchFilter, ct);

            foreach (var match in matches)
            {
                if (limit.HasValue && enqueuedCount >= limit.Value)
                {
                    break;
                }

                var matchId = match.RowKey;
                var messageJson = JsonSerializer.Serialize(new { MatchId = matchId, TournamentId = tournamentId });
                var bytes = System.Text.Encoding.UTF8.GetBytes(messageJson);
                await queueClient.SendMessageAsync(Convert.ToBase64String(bytes), ct);

                enqueuedCount++;
            }

            if (limit.HasValue && enqueuedCount >= limit.Value)
            {
                break;
            }
        }

        return enqueuedCount;
    }
}
