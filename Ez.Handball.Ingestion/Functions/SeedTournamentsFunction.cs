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
        var startYear = int.TryParse(seasonParam, out var parsed) ? parsed : DateTime.UtcNow.Year;
        var season = SeasonLabel.Format(startYear);

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

        await UpsertSeasonAsync(season, startYear);

        return (season, TournamentDefinitions.Count);
    }

    private async Task UpsertSeasonAsync(string label, int startYear)
    {
        var existing = await _tableWriter.QueryAsync<SeasonEntity>("Seasons", "PartitionKey eq 'season'");

        // Newest-season guard: the seeded season becomes current only if it is at
        // least as new as every OTHER known season (an empty table counts as newest).
        var maxOtherStartYear = existing
            .Where(s => s.RowKey != label)
            .Select(s => s.StartYear)
            .DefaultIfEmpty(int.MinValue)
            .Max();
        var becomesCurrent = startYear >= maxOtherStartYear;

        await _tableWriter.UpsertAsync("Seasons", new SeasonEntity
        {
            PartitionKey = "season",
            RowKey = label,
            StartYear = startYear,
            IsCurrent = becomesCurrent
        });

        if (becomesCurrent)
        {
            foreach (var other in existing.Where(s => s.RowKey != label && s.IsCurrent))
            {
                other.IsCurrent = false;
                await _tableWriter.UpsertAsync("Seasons", other);
            }
        }
    }
}
