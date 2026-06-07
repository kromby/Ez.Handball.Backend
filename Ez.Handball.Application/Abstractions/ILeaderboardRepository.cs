using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public enum LeaderboardMetric
{
    Goals,
    YellowCards,
    TwoMinuteSuspensions,
    RedCards,
    Games
}

// Edge → use case. Carries the raw, unresolved scope.
public sealed record LeaderboardRequest(
    LeaderboardMetric Metric,
    string? Season,
    string? TournamentId,
    string? CompetitionId,
    TournamentType? Type,
    string? Gender);

// Use case → repository. TournamentIds is the resolved set (null/empty = whole season).
public sealed record LeaderboardQuery(
    LeaderboardMetric Metric,
    string? Season,
    IReadOnlyList<string>? TournamentIds,
    string? Gender);

public interface ILeaderboardRepository
{
    // Returns the FULLY ranked list (Rank assigned 1..N). Paging happens in the use case.
    Task<IReadOnlyList<LeaderboardEntry>> GetRankedAsync(LeaderboardQuery q, CancellationToken ct);
}
