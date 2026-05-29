using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IMatchPlayerLinesRepository
{
    // Per-player stat lines for a match, grouped by teamId. Only teams that have at
    // least one stat line for this match appear; each team's lines are ordered.
    Task<IReadOnlyDictionary<string, IReadOnlyList<MatchPlayerLine>>> GetByMatchAsync(
        string matchId, CancellationToken ct);
}
