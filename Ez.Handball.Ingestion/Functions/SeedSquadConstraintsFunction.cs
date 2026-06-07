using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedSquadConstraintsFunction
{
    // Fantasy squad constraints. startingCap = a new manager's cash; maxSquadSize caps the
    // roster; posLimit:{Position} caps players per position. PLACEHOLDER position vocabulary —
    // must be reconciled with real Player.Position values (owner review). Tunable config.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConstraintDefinitions =
    [
        ("fantasy-squad-v1", "startingCap",  "100000000"),
        ("fantasy-squad-v1", "currency",     "ISK"),
        ("fantasy-squad-v1", "maxSquadSize", "15"),
        ("fantasy-squad-v1", "posLimit:GK",  "2"),
        ("fantasy-squad-v1", "posLimit:LW",  "2"),
        ("fantasy-squad-v1", "posLimit:RW",  "2"),
        ("fantasy-squad-v1", "posLimit:LB",  "3"),
        ("fantasy-squad-v1", "posLimit:CB",  "3"),
        ("fantasy-squad-v1", "posLimit:RB",  "3"),
        ("fantasy-squad-v1", "posLimit:P",   "2"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedSquadConstraintsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedSquadConstraints")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/squad-constraints")] HttpRequestData req,
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
