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
    double AvgRedCards,
    // HBStatz sums — 0 where HBStatz has no report for the matches.
    int TotalAssists = 0,
    int TotalSteals = 0,
    int TotalBlocks = 0,
    int TotalSaves = 0,
    // Fantasy points for these games under the current scoring rule set; null when the
    // rule set can't be loaded. Filled in by the use case, not the repository.
    double? Points = null);

public sealed record PlayerHistoryTotals(
    int Games,
    int TotalGoals,
    int TotalYellowCards,
    int TotalTwoMinuteSuspensions,
    int TotalRedCards,
    double AvgGoals,
    double AvgYellowCards,
    double AvgTwoMinuteSuspensions,
    double AvgRedCards,
    int TotalAssists = 0,
    int TotalSteals = 0,
    int TotalBlocks = 0,
    int TotalSaves = 0,
    double? Points = null);

public sealed record PlayerHistory(
    IReadOnlyList<PlayerHistoryEntry> Entries,
    PlayerHistoryTotals? Totals);   // Totals is null iff Entries is empty.
