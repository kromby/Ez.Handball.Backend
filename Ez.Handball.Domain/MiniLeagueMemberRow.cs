namespace Ez.Handball.Domain;

// One row from an unscoped scan of MiniLeagueMembers — a member row paired with the league it belongs to.
public sealed record MiniLeagueMemberRow(string LeagueId, MiniLeagueMember Member);
