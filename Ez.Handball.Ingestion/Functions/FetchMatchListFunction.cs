using System.Net;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public record SyncResult(int Synced, IReadOnlyList<string> Failed);

public class FetchMatchListFunction
{
    private readonly IHsiApiClient _apiClient;
    private readonly IBlobArchiver _blobArchiver;
    private readonly ITableWriter _tableWriter;

    public FetchMatchListFunction(IHsiApiClient apiClient, IBlobArchiver blobArchiver, ITableWriter tableWriter)
    {
        _apiClient = apiClient;
        _blobArchiver = blobArchiver;
        _tableWriter = tableWriter;
    }

    [Function("FetchMatchList")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sync")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<FetchMatchListFunction>();
        var result = await SyncAsync(logger);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<SyncResult> SyncAsync(ILogger? logger = null)
    {
        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>("Tournaments", string.Empty);
        var synced = 0;
        var failed = new List<string>();

        foreach (var tournament in tournaments)
        {
            try
            {
                var json = await _apiClient.GetTournamentMatchesJsonAsync(tournament.RowKey);
                await _blobArchiver.SaveAsync($"tournaments/{tournament.RowKey}/matches.json", json);
                synced++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to sync tournament {TournamentId}", tournament.RowKey);
                failed.Add(tournament.RowKey);
            }
        }

        return new SyncResult(synced, failed);
    }
}
