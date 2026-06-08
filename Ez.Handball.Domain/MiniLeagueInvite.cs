namespace Ez.Handball.Domain;

public sealed record MiniLeagueInvite(
    string Token,
    string LeagueId,
    string CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
