namespace Ez.Handball.Domain;

// A team within a full match box score: header plus its per-player stat lines.
public sealed record MatchTeam(
    string TeamId,
    string ClubId,
    string? ClubName,
    LineScore Score,
    IReadOnlyList<MatchPlayerLine> Players);
