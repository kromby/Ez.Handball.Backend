using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

// One player's scope-aggregated stats plus identity + position. The use case
// turns these into rating + price; the repository does NOT price anything.
public sealed record PooledPlayer(
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    string Position,
    AggregatedStats Stats);

// Use case → repository. TournamentIds is the resolved scope: null = whole-season
// scan; empty = scope matched no tournaments (repository returns nothing).
public sealed record PlayerPoolQuery(
    string? Season,
    IReadOnlyList<string>? TournamentIds,
    string? Gender);

public interface IPlayerPoolRepository
{
    // Returns every scoped player aggregated once (no ranking, no paging, no
    // position filter — the use case owns those).
    Task<IReadOnlyList<PooledPlayer>> GetAggregatedAsync(PlayerPoolQuery q, CancellationToken ct);
}
