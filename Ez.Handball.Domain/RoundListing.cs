namespace Ez.Handball.Domain;

public sealed record RoundListing(
    string TournamentId,
    string? TournamentName,
    string Season,
    IReadOnlyList<RoundGroup> Rounds);

public sealed record RoundGroup(
    string Round,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<RoundMatch> Matches);

public sealed record RoundMatch(
    string MatchId,
    bool Played,
    DateTimeOffset Date,
    string? Venue,
    RoundTeam Home,
    RoundTeam Away);

// Score is null for upcoming matches.
public sealed record RoundTeam(
    string TeamId,
    string ClubId,
    string? Name,
    string? LogoSrc,
    int? Score);
