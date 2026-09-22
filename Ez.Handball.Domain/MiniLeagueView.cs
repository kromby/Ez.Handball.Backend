namespace Ez.Handball.Domain;

public sealed record MiniLeagueView(
    MiniLeague League,
    IReadOnlyList<MiniLeagueMember> Members,
    IReadOnlyDictionary<string, string>? MemberTeamNames = null,
    // userId → favorite clubId. Resolved only by the league detail read; null elsewhere.
    IReadOnlyDictionary<string, string>? MemberFavoriteClubIds = null);
