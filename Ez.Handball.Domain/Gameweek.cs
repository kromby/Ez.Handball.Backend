namespace Ez.Handball.Domain;

// Lifecycle of a gameweek, derived from the clock + member-match results (see GameweekCalendarService).
// "Settled" here means all member matches are final (results complete); the per-team scoring rollup
// is driven separately and per-team scores appear once it has run.
public enum GameweekStatus
{
    Open,            // now < deadline
    DeadlineLocked,  // now >= deadline, no member match final yet
    InPlay,          // some but not all member matches final
    Settled          // all member matches final
}

// One fixture inside a gameweek. IsFinal is effective finality (#95): stored as final
// (MatchEntity.Status == "S") AND the domain clock's now is past Date + MatchFinalBufferHours.
public sealed record GameweekMatch(
    string MatchId,
    DateTimeOffset Date,
    bool IsFinal,
    string HomeTeamId,
    string AwayTeamId);

// A derived gameweek. Number is the 1-based ordinal of the round in sorted order.
// RoundLabel is the HSÍ round label and the stable key (gwKey) for all persisted gameweek state.
public sealed record Gameweek(
    int Number,
    string RoundLabel,
    string TournamentId,
    DateTimeOffset Deadline,
    GameweekStatus Status,
    IReadOnlyList<GameweekMatch> Matches);
