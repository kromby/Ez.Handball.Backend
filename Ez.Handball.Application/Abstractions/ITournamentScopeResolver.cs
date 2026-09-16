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

    /// <summary>
    /// The effective season label for a request: the given <paramref name="season"/>
    /// when non-blank, otherwise the current season's label, or null when no current
    /// season is configured. Single source of truth for the "no season requested ->
    /// current season" default used across all season-scoped reads.
    /// </summary>
    Task<string?> ResolveSeasonLabelAsync(string? season, CancellationToken ct);

    /// <summary>
    /// Resolves "the same competition, one season back" from the given scope: a
    /// single explicit <paramref name="tournamentId"/> is translated to that
    /// tournament's CompetitionId within the current season before looking up the
    /// previous season's tournament(s) for that competition. Returns null only
    /// when no previous season exists at all (no current season resolvable, or the
    /// current season is the oldest one tracked).
    /// </summary>
    Task<PreviousSeasonScope?> ResolvePreviousSeasonScopeAsync(
        string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct);
}
