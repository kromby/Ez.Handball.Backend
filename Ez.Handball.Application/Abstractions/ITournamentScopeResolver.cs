using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

/// <summary>
/// Translates a request scope into the set of tournament ids to filter player
/// stats by. Returns null when no narrowing is requested (whole-season scan),
/// an empty list when the scope matches nothing.
/// </summary>
public interface ITournamentScopeResolver
{
    Task<IReadOnlyList<string>?> ResolveTournamentIdsAsync(
        string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct);
}
