namespace Ez.Handball.Domain;

public sealed record PlayerHistoryEntry(
    string Season,
    string TournamentId,
    string? TournamentName,
    string ClubId,
    string? ClubName,
    int Games,
    int TotalGoals,
    int TotalYellowCards,
    int TotalTwoMinuteSuspensions,
    int TotalRedCards,
    double AvgGoals,
    double AvgYellowCards,
    double AvgTwoMinuteSuspensions,
    double AvgRedCards);

public sealed record PlayerHistoryTotals(
    int Games,
    int TotalGoals,
    int TotalYellowCards,
    int TotalTwoMinuteSuspensions,
    int TotalRedCards,
    double AvgGoals,
    double AvgYellowCards,
    double AvgTwoMinuteSuspensions,
    double AvgRedCards);

public sealed record PlayerHistory(
    IReadOnlyList<PlayerHistoryEntry> Entries,
    PlayerHistoryTotals? Totals);   // Totals is null iff Entries is empty.
