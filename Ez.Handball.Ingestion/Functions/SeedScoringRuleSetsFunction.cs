using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedScoringRuleSetsFunction
{
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> RuleSetDefinitions =
    [
        ("fantasy-v1", "goals",       "2"),
        ("fantasy-v1", "yellowCards", "-1"),
        ("fantasy-v1", "twoMinute",   "-2"),
        ("fantasy-v1", "redCards",    "-5"),
        ("fantasy-v1", "appearances", "1"),

        ("fantasy-v2", "goals",       "2"),
        ("fantasy-v2", "yellowCards", "-1"),
        ("fantasy-v2", "twoMinute",   "-2"),
        ("fantasy-v2", "redCards",    "-5"),
        ("fantasy-v2", "appearances", "1"),
        ("fantasy-v2", "assists",     "1"),
        ("fantasy-v2", "steals",      "1"),
        ("fantasy-v2", "blocks",      "1"),
        ("fantasy-v2", "saves",       "0.5"),

        // #160: v2 with saves raised from 0.5 to 1 — goalkeepers scored far below every other position.
        ("fantasy-v3", "goals",       "2"),
        ("fantasy-v3", "yellowCards", "-1"),
        ("fantasy-v3", "twoMinute",   "-2"),
        ("fantasy-v3", "redCards",    "-5"),
        ("fantasy-v3", "appearances", "1"),
        ("fantasy-v3", "assists",     "1"),
        ("fantasy-v3", "steals",      "1"),
        ("fantasy-v3", "blocks",      "1"),
        ("fantasy-v3", "saves",       "1"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedScoringRuleSetsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedScoringRuleSets")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/scoring-rule-sets")] HttpRequestData req,
        FunctionContext context)
    {
        var seeded = await ProcessAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded });
        return response;
    }

    public async Task<int> ProcessAsync()
    {
        foreach (var (group, key, value) in RuleSetDefinitions)
        {
            await _tableWriter.UpsertAsync("Config", new ConfigEntity
            {
                PartitionKey = group,
                RowKey = key,
                Value = value
            }, mode: TableUpdateMode.Replace); // explicit Replace keeps seeding idempotent
        }

        return RuleSetDefinitions.Count;
    }
}
