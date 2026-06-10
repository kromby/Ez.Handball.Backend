namespace Ez.Handball.Domain;

// Repository result for one tournament's fixtures. The repository returns null
// (not an empty instance) when the tournament id is unknown.
public sealed record TournamentMatches(
    string TournamentId,
    string? TournamentName,
    string Season,
    IReadOnlyList<MatchListItem> Matches);
