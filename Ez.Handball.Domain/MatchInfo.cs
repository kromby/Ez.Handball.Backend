namespace Ez.Handball.Domain;

// A match without its box score: metadata plus both team headers.
// The per-player lines are retrieved separately and composed into a MatchDetail.
public sealed record MatchInfo(
    string MatchId,
    string TournamentId,
    string? TournamentName,
    string Season,
    DateTimeOffset Date,
    string? Venue,
    int? Attendance,
    string Status,
    MatchTeamInfo HomeTeam,
    MatchTeamInfo AwayTeam);
