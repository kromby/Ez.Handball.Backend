using System.Net;
using Azure;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedPriceRuleSetsFunction
{
    // Fantasy ISK price rule set. minGames guards thin samples; blendGames is the
    // prior-season fade horizon (in current-season games) — below it, price blends
    // toward last season's same-competition rate instead of the floor band; bands
    // map points-per-game to a price: 5M floor, then +2M every 1.2 points per game
    // from 3.2, capped at 35M from 20 points per game. Thresholds/prices are tunable
    // config (scoring calibration: #27).
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> RuleSetDefinitions =
    [
        ("fantasy-price-v1", "minGames", "3"),
        ("fantasy-price-v1", "blendGames", "10"),
        ("fantasy-price-v1", "currency", "ISK"),
        ("fantasy-price-v1", "band:0",     "5000000"),
        ("fantasy-price-v1", "band:3.2",   "7000000"),
        ("fantasy-price-v1", "band:4.4",   "9000000"),
        ("fantasy-price-v1", "band:5.6",   "11000000"),
        ("fantasy-price-v1", "band:6.8",   "13000000"),
        ("fantasy-price-v1", "band:8",     "15000000"),
        ("fantasy-price-v1", "band:9.2",   "17000000"),
        ("fantasy-price-v1", "band:10.4",  "19000000"),
        ("fantasy-price-v1", "band:11.6",  "21000000"),
        ("fantasy-price-v1", "band:12.8",  "23000000"),
        ("fantasy-price-v1", "band:14",    "25000000"),
        ("fantasy-price-v1", "band:15.2",  "27000000"),
        ("fantasy-price-v1", "band:16.4",  "29000000"),
        ("fantasy-price-v1", "band:17.6",  "31000000"),
        ("fantasy-price-v1", "band:18.8",  "33000000"),
        ("fantasy-price-v1", "band:20",    "35000000"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedPriceRuleSetsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedPriceRuleSets")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/price-rule-sets")] HttpRequestData req,
        FunctionContext context)
    {
        var seeded = await ProcessAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded });
        return response;
    }

    public async Task<int> ProcessAsync()
    {
        // Upserts never remove a band, so a leftover threshold from an earlier calibration would
        // keep overriding the new steps: every band row this seed doesn't define is deleted. Prices
        // are read live, so the deletes and upserts go in one transaction per group (the rows share
        // a PartitionKey) — a buy mid-seed must never see a half-written set, e.g. only the 5M floor.
        var current = RuleSetDefinitions.Select(d => (d.Group, d.Key)).ToHashSet();
        foreach (var group in RuleSetDefinitions.Select(d => d.Group).Distinct())
        {
            var existing = await _tableWriter.QueryAsync<ConfigEntity>("Config", $"PartitionKey eq '{group}'");
            var actions = existing
                .Where(row => row.RowKey.StartsWith("band:", StringComparison.Ordinal) && !current.Contains((group, row.RowKey)))
                .Select(row => new TableTransactionAction(
                    TableTransactionActionType.Delete, new TableEntity(group, row.RowKey), ETag.All))
                .Concat(RuleSetDefinitions
                    .Where(d => d.Group == group)
                    .Select(d => new TableTransactionAction(
                        TableTransactionActionType.UpsertReplace, // Replace keeps seeding idempotent
                        new ConfigEntity { PartitionKey = group, RowKey = d.Key, Value = d.Value })))
                .ToList();

            await _tableWriter.SubmitTransactionAsync("Config", actions);
        }

        return RuleSetDefinitions.Count;
    }
}
