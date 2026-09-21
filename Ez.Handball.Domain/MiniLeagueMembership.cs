namespace Ez.Handball.Domain;

// One row from the reverse (by-user) membership index — a league a given user belongs to.
public sealed record MiniLeagueMembership(
    string LeagueId,
    string Role,
    DateTimeOffset JoinedAt);
