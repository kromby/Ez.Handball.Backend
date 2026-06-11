using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public record BootstrapRetiredResult(string Season, int Marked);

public class BootstrapRetiredFunction
{
    private readonly ITableWriter _tableWriter;

    public BootstrapRetiredFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("BootstrapRetired")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/bootstrap-retired")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<BootstrapRetiredFunction>();
        var result = await ProcessAsync(logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<BootstrapRetiredResult> ProcessAsync(ILogger? logger = null, CancellationToken ct = default)
    {
        // 1. Latest season = lexical max of the distinct Tournaments partition keys
        //    (the YYYY-YY label format sorts correctly: "2025-26" > "2024-25").
        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>("Tournaments", null!, ct);
        var latestSeason = tournaments
            .Select(t => t.PartitionKey)
            .Where(s => !string.IsNullOrEmpty(s))
            .DefaultIfEmpty(string.Empty)
            .Max(StringComparer.Ordinal)!;

        if (string.IsNullOrEmpty(latestSeason))
            return new BootstrapRetiredResult(string.Empty, 0);

        // 2. Player ids that appear in PlayerStats for that season.
        var stats = await _tableWriter.QueryAsync<PlayerStatEntity>(
            "PlayerStats", $"Season eq '{latestSeason}'", ct);
        var played = stats.Select(s => s.RowKey).ToHashSet(StringComparer.Ordinal);

        // 3. Every player with no stats that season gets Retired = true. We write
        //    back the FULL entity we read (not a partial one) — a Merge upsert of a
        //    partial PlayerEntity would blank Name/Position/etc. to their empty-string
        //    defaults. Only ever sets true, so re-runs never clobber manual edits.
        var players = await _tableWriter.QueryAsync<PlayerEntity>("Players", null!, ct);
        var marked = 0;
        foreach (var p in players.Where(p => !played.Contains(p.RowKey)))
        {
            p.Retired = true;
            await _tableWriter.UpsertAsync("Players", p, ct, TableUpdateMode.Merge);
            marked++;
        }

        logger?.LogInformation("Bootstrap-retired complete: season={Season}, marked={Marked}", latestSeason, marked);
        return new BootstrapRetiredResult(latestSeason, marked);
    }
}
