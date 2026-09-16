using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetPlayersMissingPositionUseCase
{
    Task<IReadOnlyList<Player>> ExecuteAsync(string? clubId, string? gender, CancellationToken ct);
}

// When no explicit clubId is given, scoped to clubs playing in an actively-ingested
// tournament (Active=true and Ingest=true). Most competitions besides the top division
// aren't synced for the current season yet, so their entire rosters have no PlayerStats
// and look indistinguishable from "missing position" — without this scoping they'd flood
// the admin worklist even though they're not retired, just not-yet-tracked. An explicit
// clubId is an intentional admin request and bypasses this scoping.
public class GetPlayersMissingPositionUseCase : IGetPlayersMissingPositionUseCase
{
    private readonly IPlayerRepository _players;
    private readonly ITournamentRepository _tournaments;
    private readonly IMatchRepository _matches;

    public GetPlayersMissingPositionUseCase(
        IPlayerRepository players, ITournamentRepository tournaments, IMatchRepository matches)
    {
        _players = players;
        _tournaments = tournaments;
        _matches = matches;
    }

    public async Task<IReadOnlyList<Player>> ExecuteAsync(string? clubId, string? gender, CancellationToken ct)
    {
        var candidates = await _players.ListMissingPositionAsync(clubId, gender, ct);
        if (!string.IsNullOrWhiteSpace(clubId)) return candidates;

        var allowedClubIds = await ResolveActivelyIngestedClubIdsAsync(gender, ct);
        return candidates.Where(p => allowedClubIds.Contains(p.ClubId)).ToList();
    }

    private async Task<HashSet<string>> ResolveActivelyIngestedClubIdsAsync(string? gender, CancellationToken ct)
    {
        var tournaments = await _tournaments.ListAllAsync(ct);
        var ingested = tournaments
            .Where(t => t.Active && t.Ingest)
            .Where(t => string.IsNullOrWhiteSpace(gender) || t.Gender == gender);

        var clubIds = new HashSet<string>();
        foreach (var tournament in ingested)
        {
            var matches = await _matches.ListByTournamentAsync(tournament.TournamentId, ct);
            if (matches is null) continue;

            foreach (var match in matches.Matches)
            {
                clubIds.Add(match.Home.ClubId);
                clubIds.Add(match.Away.ClubId);
            }
        }

        return clubIds;
    }
}
