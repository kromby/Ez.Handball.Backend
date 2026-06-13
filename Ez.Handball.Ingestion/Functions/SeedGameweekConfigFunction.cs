using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedGameweekConfigFunction
{
    // The fantasy calendar config. tournamentId names the tournament whose HSÍ rounds become
    // gameweeks; lockOffsetHours is how far before first throw-off a gameweek locks (owner-tunable);
    // the version keys point at which scoring rule set (#27) and lineup constraints (#61) the rollup uses.
    // PLACEHOLDER tournamentId 8444 (Olís deild karla) — owner must confirm per season/environment.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConfigDefinitions =
    [
        ("fantasy-gameweek-v1", "tournamentId",             "8444"),
        ("fantasy-gameweek-v1", "lockOffsetHours",          "1"),
        ("fantasy-gameweek-v1", "scoringRuleSetVersion",    "1"),
        ("fantasy-gameweek-v1", "lineupConstraintsVersion", "1"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedGameweekConfigFunction(ITableWriter tableWriter) => _tableWriter = tableWriter;

    [Function("SeedGameweekConfig")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/gameweek-config")] HttpRequestData req,
        FunctionContext context)
    {
        var seeded = await ProcessAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded });
        return response;
    }

    public async Task<int> ProcessAsync()
    {
        foreach (var (group, key, value) in ConfigDefinitions)
        {
            await _tableWriter.UpsertAsync("Config", new ConfigEntity
            {
                PartitionKey = group,
                RowKey = key,
                Value = value
            }, mode: TableUpdateMode.Replace);
        }
        return ConfigDefinitions.Count;
    }
}
