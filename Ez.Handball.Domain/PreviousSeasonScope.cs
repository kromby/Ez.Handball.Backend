namespace Ez.Handball.Domain;

// The previous season's resolved scope for a "same competition, one season back"
// lookup. TournamentIds follows the same convention as
// ITournamentScopeResolver.ResolveTournamentIdsAsync: null = no narrowing (scan
// the whole previous season), non-null (possibly empty) = narrowed to these ids.
public sealed record PreviousSeasonScope(
    string SeasonLabel,
    IReadOnlyList<string>? TournamentIds);
