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
}
