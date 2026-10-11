namespace Ez.Handball.Domain;

// StartRound is the round label standings start counting from (Backend#154), so a league
// created mid-season isn't decided by rounds its members never played. Null counts every round.
public sealed record MiniLeague(
    string Id,
    string Name,
    string Season,
    string CreatorUserId,
    DateTimeOffset CreatedAt,
    string? StartRound = null);
