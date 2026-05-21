using System.Net;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedTournamentsFunction
{
    private static readonly IReadOnlyList<(string Id, string Name, string Gender, string Division, bool Enabled)> TournamentDefinitions =
    [
        ("8444", "Olís deild karla",              "karlar", "1",       true),
        ("8434", "Olís deild kvenna",             "kvenna", "1",       false),
        ("8427", "Olís deild úrslit karla",       "karlar", "1-final", false),
        ("8430", "Olís deild úrslit kvenna",      "kvenna", "1-final", false),
        ("8424", "Grill 66 deild karla",          "karlar", "2",       false),
        ("8443", "Grill 66 deild kvenna",         "kvenna", "2",       false),
        ("8441", "Grill 66 deild umspil karla",   "karlar", "2-final", false),
        ("8422", "Grill 66 deild umspil kvenna",  "kvenna", "2-final", false),
        ("8437", "Powerade bikar karla",          "karlar", "cup",     false),
        ("8436", "Powerade bikar kvenna",         "kvenna", "cup",     false),
    ];

    private readonly ITableWriter _tableWriter;

    public SeedTournamentsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("SeedTournaments")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "seed/tournaments")] HttpRequestData req,
        FunctionContext context)
    {
        var season = req.Query["season"] ?? DateTime.UtcNow.Year.ToString();

        foreach (var (id, name, gender, division, enabled) in TournamentDefinitions)
        {
            await _tableWriter.UpsertAsync("Tournaments", new TournamentEntity
            {
                PartitionKey = season,
                RowKey = id,
                Name = name,
                Gender = gender,
                Division = division,
                Enabled = enabled
            });
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { season, seeded = TournamentDefinitions.Count });
        return response;
    }
}
