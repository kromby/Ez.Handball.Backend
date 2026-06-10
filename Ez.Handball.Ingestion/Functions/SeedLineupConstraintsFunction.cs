using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedLineupConstraintsFunction
{
    // Fantasy lineup (formation) constraints. starterCount = size of the starting 7;
    // captainMultiplier is read by scoring (#60); startMin/startMax:{Position} bound how many
    // starters may play each position (GK min=max=1 = exactly one keeper). PLACEHOLDER position
    // vocabulary — must be reconciled with real Player.Position values (owner review). Tunable.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConstraintDefinitions =
    [
        ("fantasy-lineup-v1", "starterCount",      "7"),
        ("fantasy-lineup-v1", "captainMultiplier", "2"),
        ("fantasy-lineup-v1", "captainRequired",   "true"),
        ("fantasy-lineup-v1", "viceRequired",      "false"),
        ("fantasy-lineup-v1", "startMin:GK", "1"), ("fantasy-lineup-v1", "startMax:GK", "1"),
        ("fantasy-lineup-v1", "startMin:LW", "0"), ("fantasy-lineup-v1", "startMax:LW", "2"),
        ("fantasy-lineup-v1", "startMin:RW", "0"), ("fantasy-lineup-v1", "startMax:RW", "2"),
        ("fantasy-lineup-v1", "startMin:LB", "0"), ("fantasy-lineup-v1", "startMax:LB", "3"),
        ("fantasy-lineup-v1", "startMin:CB", "0"), ("fantasy-lineup-v1", "startMax:CB", "2"),
        ("fantasy-lineup-v1", "startMin:RB", "0"), ("fantasy-lineup-v1", "startMax:RB", "3"),
        ("fantasy-lineup-v1", "startMin:LP", "0"), ("fantasy-lineup-v1", "startMax:LP", "2"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedLineupConstraintsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedLineupConstraints")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/lineup-constraints")] HttpRequestData req,
        FunctionContext context)
    {
        var seeded = await ProcessAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { seeded });
        return response;
    }

    public async Task<int> ProcessAsync()
    {
        foreach (var (group, key, value) in ConstraintDefinitions)
        {
            await _tableWriter.UpsertAsync("Config", new ConfigEntity
            {
                PartitionKey = group,
                RowKey = key,
                Value = value
            }, mode: TableUpdateMode.Replace); // explicit Replace keeps seeding idempotent
        }

        return ConstraintDefinitions.Count;
    }
}
