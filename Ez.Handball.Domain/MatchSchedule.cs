namespace Ez.Handball.Domain;

// The archived hsi.is match list for one tournament, as of the last successful sync.
// Source of truth for "what games are scheduled" — independent of what has actually
// been parsed into the Matches table, so the two can be cross-checked to reveal gaps.
public sealed record MatchSchedule(
    IReadOnlyList<ScheduledMatch> Matches,
    DateTimeOffset LastSyncedAt);

public sealed record ScheduledMatch(
    string MatchId,
    string Round,
    DateTimeOffset Date,
    string? Venue,
    string HomeTeamName,
    string AwayTeamName,
    string HsiStatus); // "S" (finished) | "O" (upcoming) — hsi.is's own status code
