using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

// Manual fallback for players HBStatz can't reach: their tournament isn't HBStatz-enabled,
// reconciliation never succeeds, or they never appear in a synced match. Never conflicts with
// the automated aggregator (Backend#106), since that only writes when it has actual observations.
public record SetPlayerPositionRequest(string PlayerId, string Position, string? PositionSecondary);

public record SetPlayerPositionResult(string PlayerId, string Status, string? Detail);

public record SetPlayerPositionBatchResult(bool DryRun, IReadOnlyList<SetPlayerPositionResult> Results);

public class SetPlayerPositionFunction
{
    private readonly ITableWriter _tableWriter;

    public SetPlayerPositionFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SetPlayerPosition")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/set-position")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<SetPlayerPositionFunction>();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var requests = await JsonSerializer.DeserializeAsync<List<SetPlayerPositionRequest>>(
            req.Body, options, context.CancellationToken) ?? [];

        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);
        var result = await ProcessAsync(requests, dryRun, logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<SetPlayerPositionBatchResult> ProcessAsync(
        IReadOnlyList<SetPlayerPositionRequest> requests, bool dryRun, ILogger? logger = null, CancellationToken ct = default)
    {
        var results = new List<SetPlayerPositionResult>();

        foreach (var request in requests)
        {
            try
            {
                results.Add(await SetAsync(request, dryRun, ct));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "SetPlayerPosition failed for {PlayerId}", request.PlayerId);
                results.Add(new SetPlayerPositionResult(request.PlayerId, "Error", ex.Message));
            }
        }

        return new SetPlayerPositionBatchResult(dryRun, results);
    }

    private async Task<SetPlayerPositionResult> SetAsync(SetPlayerPositionRequest request, bool dryRun, CancellationToken ct)
    {
        if (!PositionVocabulary.Codes.Contains(request.Position))
            return new SetPlayerPositionResult(request.PlayerId, "InvalidPosition", $"'{request.Position}' is not a valid position code.");

        if (request.PositionSecondary is not null && !PositionVocabulary.Codes.Contains(request.PositionSecondary))
            return new SetPlayerPositionResult(request.PlayerId, "InvalidPositionSecondary", $"'{request.PositionSecondary}' is not a valid position code.");

        var players = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"RowKey eq '{Escape(request.PlayerId)}'", ct);
        var player = players.FirstOrDefault();
        if (player is null)
            return new SetPlayerPositionResult(request.PlayerId, "PlayerNotFound", null);

        var newSecondary = request.PositionSecondary ?? string.Empty;
        var detail = $"{player.Name}: {player.Position}/{player.PositionSecondary} -> {request.Position}/{newSecondary}";
        if (dryRun) return new SetPlayerPositionResult(request.PlayerId, "DryRun", detail);

        player.Position = request.Position;
        player.PositionSecondary = newSecondary;
        await _tableWriter.UpsertAsync("Players", player, ct, TableUpdateMode.Merge);

        return new SetPlayerPositionResult(request.PlayerId, "Applied", detail);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
