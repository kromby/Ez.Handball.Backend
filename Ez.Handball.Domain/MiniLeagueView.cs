namespace Ez.Handball.Domain;

public sealed record MiniLeagueView(
    MiniLeague League,
    IReadOnlyList<MiniLeagueMember> Members,
    IReadOnlyDictionary<string, string>? MemberTeamNames = null);
