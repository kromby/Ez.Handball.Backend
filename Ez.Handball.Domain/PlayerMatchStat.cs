namespace Ez.Handball.Domain;

// One player's line in one match, with the match context the player page shows alongside it.
public sealed record PlayerMatchStat(
    PlayerStat Stat,
    DateTimeOffset? Date,
    MatchListTeam? Opponent,
    // Fantasy points for this match under the current scoring rule set; null when the
    // rule set can't be loaded.
    double? Points);
