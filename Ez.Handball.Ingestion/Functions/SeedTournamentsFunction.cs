using System.Net;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

public class SeedTournamentsFunction
{
    internal static readonly IReadOnlyList<(string Id, string Name, string Gender, string Division, bool Enabled, int Priority)> TournamentDefinitions =
    [
        ("8444", "Olís deild karla",              "karlar", "1",       true,  10),
        ("8434", "Olís deild kvenna",             "kvenna", "1",       false, 10),
        ("8427", "Olís deild úrslit karla",       "karlar", "1-final", false, 20),
        ("8430", "Olís deild úrslit kvenna",      "kvenna", "1-final", false, 20),
        ("8424", "Grill 66 deild karla",          "karlar", "2",       false, 30),
        ("8443", "Grill 66 deild kvenna",         "kvenna", "2",       false, 30),
        ("8441", "Grill 66 deild umspil karla",   "karlar", "2-final", false, 40),
        ("8422", "Grill 66 deild umspil kvenna",  "kvenna", "2-final", false, 40),
        ("8437", "Powerade bikar karla",          "karlar", "cup",     false, 50),
        ("8436", "Powerade bikar kvenna",         "kvenna", "cup",     false, 50),
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
        var (season, seeded) = await ProcessAsync(req.Query["season"]);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { season, seeded });
        return response;
    }

    public async Task<(string Season, int Seeded)> ProcessAsync(string? seasonParam)
    {
        var season = SeasonLabel.Resolve(seasonParam, DateTime.UtcNow.Year);

        foreach (var (id, name, gender, division, enabled, priority) in TournamentDefinitions)
        {
            await _tableWriter.UpsertAsync("Tournaments", new TournamentEntity
            {
                PartitionKey = season,
                RowKey = id,
                Name = name,
                Gender = gender,
                Division = division,
                Enabled = enabled,
                Priority = priority
            });
        }

        return (season, TournamentDefinitions.Count);
    }
}
