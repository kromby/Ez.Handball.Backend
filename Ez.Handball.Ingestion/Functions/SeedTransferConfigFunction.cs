using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedTransferConfigFunction
{
    // The per-round transfer rules (#63/#168): one buy and one sell free per round, up to one unused
    // free transfer banked, 4 points per transfer beyond that. startsFromRound is owner-tunable:
    // set it to the first round the rules should apply to (rounds before it are unlimited and bank
    // nothing) — for the 2026/27 season that is the first round still Open when this ships.
    internal static readonly IReadOnlyList<(string Group, string Key, string Value)> ConfigDefinitions =
    [
        ("fantasy-transfers-v1", "freeTransfersPerRound",  "1"),
        ("fantasy-transfers-v1", "maxBankedFreeTransfers", "1"),
        ("fantasy-transfers-v1", "pointsPerExtraTransfer", "4"),
        ("fantasy-transfers-v1", "startsFromRound",        "7"),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedTransferConfigFunction(ITableWriter tableWriter) => _tableWriter = tableWriter;

    [Function("SeedTransferConfig")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/transfer-config")] HttpRequestData req,
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
