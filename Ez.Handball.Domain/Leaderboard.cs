namespace Ez.Handball.Domain;

public sealed record LeaderboardEntry(
    int Rank,
    string PlayerId,
    string? Name,
    string ClubId,
    string? ClubName,
    string Gender,
    int Games,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards,
    double AvgGoals);

public sealed record Leaderboard(
    string Metric,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<LeaderboardEntry> Entries);
