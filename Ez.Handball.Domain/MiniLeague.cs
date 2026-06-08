namespace Ez.Handball.Domain;

public sealed record MiniLeague(
    string Id,
    string Name,
    string Season,
    string CreatorUserId,
    DateTimeOffset CreatedAt);
