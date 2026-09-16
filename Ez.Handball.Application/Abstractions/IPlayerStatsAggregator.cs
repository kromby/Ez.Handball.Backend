using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerStatsAggregator
{
    // Resolves the season (current when null/blank), resolves the tournament-id scope
    // (tournament / competition / type via ITournamentScopeResolver), and sums the
    // player's scoped PlayerStats rows into AggregatedStats. No matching rows => zeros.
    Task<AggregatedStats> AggregateAsync(
        string playerId, string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct);

    // Same-competition, one-season-back aggregate for a player, or null when
    // there's no previous season, the competition didn't exist in it, or the
    // player has no rows in it. Callers apply their own "is this sample usable"
    // threshold (e.g. FantasyPricing.Compute checks Games against MinGames).
    Task<AggregatedStats?> AggregatePreviousSeasonAsync(
        string playerId, string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct);
}
