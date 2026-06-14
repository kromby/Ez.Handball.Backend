namespace Ez.Handball.Domain;

// A lightweight projection of a settled gameweek score — points only, no breakdown.
public sealed record GameweekScoreSummary(string TeamId, string RoundLabel, double Points);

// One manager's row in the standings.
// PreviousRank/RankDelta are null for a manager who first appears in the latest round.
// RankDelta = PreviousRank − Rank, so a positive value means the manager climbed.
public sealed record ManagerStanding(
    int Rank,
    int? PreviousRank,
    int? RankDelta,
    string TeamId,
    string TeamName,
    string Color,
    double TotalPoints,
    double RoundPoints);

// The paginated response returned by both standings endpoints.
public sealed record ManagerStandings(
    int Total,
    int Offset,
    int Limit,
    string? LatestRoundLabel,
    IReadOnlyList<ManagerStanding> Entries);

// The full ranked list produced by ManagerStandingsRanker (pre-pagination).
public sealed record RankedManagers(
    string? LatestRoundLabel,
    IReadOnlyList<ManagerStanding> Entries);
