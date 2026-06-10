namespace Ez.Handball.Domain;

// A lightweight fixture row for the round listing — no player lines. Score is the
// raw stored score; whether it is surfaced is decided by status downstream.
public sealed record MatchListItem(
    string MatchId,
    string Round,
    DateTimeOffset Date,
    string? Venue,
    string Status,
    MatchListTeam Home,
    MatchListTeam Away);

public sealed record MatchListTeam(
    string TeamId,
    string ClubId,
    string? ClubName,
    string? LogoSrc,
    int Score);
