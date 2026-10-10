namespace Ez.Handball.Domain;

// A lightweight projection of a settled gameweek score — points only, no breakdown.
public sealed record GameweekScoreSummary(string TeamId, string RoundLabel, double Points);

// One settled round in a manager's history. TotalPoints is the running total after this round.
public sealed record RoundScore(string RoundLabel, double Points, double TotalPoints);

// One manager's row in the standings.
// PreviousRank/RankDelta are null for a manager who first appears in the latest round.
// RankDelta = PreviousRank − Rank, so a positive value means the manager climbed.
// Rounds lists the manager's settled rounds oldest-first; RoundsPlayed is its length and
// AveragePoints is TotalPoints / RoundsPlayed (0 before the first settled round).
// RoundsWon counts the rounds where this manager had the top score among the ranked set
// (ties all win); WonLatestRound is whether that includes LatestRoundLabel.
public sealed record ManagerStanding(
    int Rank,
    int? PreviousRank,
    int? RankDelta,
    string TeamId,
    string TeamName,
    string Color,
    double TotalPoints,
    double RoundPoints,
    int RoundsPlayed,
    double AveragePoints,
    IReadOnlyList<RoundScore> Rounds,
    int RoundsWon,
    bool WonLatestRound);

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
