using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedSalaryRuleSetsFunction
{
    // Fantasy ISK price rule set. minGames guards thin samples; bands map points-per-game
    // to a price. Thresholds/prices are tunable config (scoring calibration: #27).
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> RuleSetDefinitions =
    [
        ("fantasy-price-v1", "minGames", "3"),
        ("fantasy-price-v1", "currency", "ISK"),
        ("fantasy-price-v1", "band:0",   "5000000"),
        ("fantasy-price-v1", "band:3",   "10000000"),
        ("fantasy-price-v1", "band:6",   "20000000"),
        ("fantasy-price-v1", "band:9",   "35000000"),
        ("fantasy-price-v1", "band:12",  "50000000"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedSalaryRuleSetsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedSalaryRuleSets")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/salary-rule-sets")] HttpRequestData req,
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
