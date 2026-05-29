namespace Ez.Handball.Domain;

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

public sealed record MatchTeam(
    string TeamId,
    string ClubId,
    string? ClubName,
    LineScore Score,
    IReadOnlyList<MatchPlayerLine> Players);

public sealed record LineScore(int FirstHalf, int SecondHalf, int Final);

public sealed record MatchPlayerLine(
    string PlayerId,
    string? Name,
    string? JerseyNumber,
    string? Position,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards);
