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

public sealed record LeaderboardQuery(
    LeaderboardMetric Metric,
    string? Season,
    string? TournamentId,
    string? Gender);

public interface ILeaderboardRepository
{
    // Returns the FULLY ranked list (Rank assigned 1..N). Paging happens in the use case.
    Task<IReadOnlyList<LeaderboardEntry>> GetRankedAsync(LeaderboardQuery q, CancellationToken ct);
}
