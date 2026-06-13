namespace Ez.Handball.Domain;

// The per-season fantasy calendar config (Config group "fantasy-gameweek-v{Version}").
// TournamentId names the tournament whose HSÍ rounds become gameweeks. LockOffsetHours is
// how many hours before the first throw-off a gameweek's deadline falls. The two version
// fields point at which ScoringRuleSet (#27) and LineupConstraints (#61) the rollup uses.
public sealed record GameweekConfig(
    int Version,
    string TournamentId,
    double LockOffsetHours,
    int ScoringRuleSetVersion,
    int LineupConstraintsVersion);
