using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetAdminGameStatusUseCase
{
    Task<IReadOnlyList<AdminTournamentGames>> ExecuteAsync(string? season, CancellationToken ct);
}

// Cross-checks the archived hsi.is schedule against the Matches table so an admin can
// spot games the ingestion pipeline missed. Scoped to Active tournaments only — same
// scope as the public /api/tournaments list.
public class GetAdminGameStatusUseCase : IGetAdminGameStatusUseCase
{
    private readonly ITournamentRepository _tournaments;
    private readonly ISeasonRepository _seasons;
    private readonly IMatchScheduleRepository _schedules;
    private readonly IMatchRepository _matches;

    public GetAdminGameStatusUseCase(
        ITournamentRepository tournaments, ISeasonRepository seasons,
        IMatchScheduleRepository schedules, IMatchRepository matches)
    {
        _tournaments = tournaments;
        _seasons = seasons;
        _schedules = schedules;
        _matches = matches;
    }

    public async Task<IReadOnlyList<AdminTournamentGames>> ExecuteAsync(string? season, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            var seasons = await _seasons.ListAsync(ct);
            var current = seasons.FirstOrDefault(s => s.IsCurrent);
            if (current is null) return Array.Empty<AdminTournamentGames>();
            season = current.Label;
        }

        var tournaments = await _tournaments.ListActiveBySeasonAsync(season, ct);
        var result = new List<AdminTournamentGames>();
        foreach (var tournament in tournaments)
            result.Add(await BuildAsync(tournament, ct));

        return result;
    }

    private async Task<AdminTournamentGames> BuildAsync(Tournament tournament, CancellationToken ct)
    {
        var schedule = await _schedules.GetAsync(tournament.TournamentId, ct);
        var ingested = await _matches.ListByTournamentAsync(tournament.TournamentId, ct);
        var ingestedById = (ingested?.Matches ?? Array.Empty<MatchListItem>())
            .ToDictionary(m => m.MatchId);

        var rounds = (schedule?.Matches ?? Array.Empty<ScheduledMatch>())
            .GroupBy(m => m.Round)
            .Select(g => new AdminRoundGames(
                g.Key,
                g.OrderBy(m => m.Date).Select(m => ToGameStatus(m, ingestedById)).ToList()))
            .OrderBy(r => RoundOrder.Key(r.Round))
            .ThenBy(r => r.Round, StringComparer.Ordinal)
            .ToList();

        return new AdminTournamentGames(
            tournament.TournamentId, tournament.Name, tournament.CompetitionName,
            schedule?.LastSyncedAt, rounds);
    }

    private static AdminGameStatus ToGameStatus(ScheduledMatch m, IReadOnlyDictionary<string, MatchListItem> ingestedById)
    {
        var ingested = ingestedById.TryGetValue(m.MatchId, out var match) ? match : null;
        return new AdminGameStatus(
            MatchId: m.MatchId,
            Date: m.Date,
            Venue: m.Venue,
            HomeTeamName: m.HomeTeamName,
            AwayTeamName: m.AwayTeamName,
            Status: m.HsiStatus == "S" ? "played" : "upcoming",
            Ingested: ingested is not null,
            HbStatzIngested: ingested?.HbStatzSyncedAt is not null);
    }
}
