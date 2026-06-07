using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerStatsAggregator
{
    // Resolves the season (current when null/blank) and sums the player's scoped
    // PlayerStats rows into AggregatedStats. No matching rows => all zeros.
    Task<AggregatedStats> AggregateAsync(
        string playerId, string? season, string? tournamentId, CancellationToken ct);
}
