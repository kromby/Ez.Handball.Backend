namespace Ez.Handball.Domain;

// A team header without player lines: identity plus its line score.
public sealed record MatchTeamInfo(
    string TeamId,
    string ClubId,
    string? ClubName,
    LineScore Score);
