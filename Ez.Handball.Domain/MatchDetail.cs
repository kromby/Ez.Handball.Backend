namespace Ez.Handball.Domain;

// Full match resource (box score): metadata plus both teams with their player lines.
public sealed record MatchDetail(
    string MatchId,
    string TournamentId,
    string? TournamentName,
    string Season,
    DateTimeOffset Date,
    string? Venue,
    int? Attendance,
    string Status,
    MatchTeam HomeTeam,
    MatchTeam AwayTeam);
