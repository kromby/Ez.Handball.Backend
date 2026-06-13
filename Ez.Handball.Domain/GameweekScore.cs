namespace Ez.Handball.Domain;

// One player's contribution to a gameweek score. Played = appeared in a member match.
// AutoSubbedIn = a bench player promoted because a starter didn't play. Multiplier is the factor
// applied to this player's raw points (2.0 for the effective captain, else 1.0).
public sealed record GameweekPlayerScore(
    string PlayerId,
    double RawPoints,
    double Points,          // RawPoints * Multiplier, and 0 for a non-playing unsubbed starter
    bool Played,
    bool AutoSubbedIn,
    bool CaptainApplied,
    double Multiplier);

// A settled gameweek score for one team. Points = Σ Breakdown.Points over effective starters.
public sealed record GameweekScore(
    string TeamId,
    string RoundLabel,
    double Points,
    string? CaptainPlayerId,    // the effective captain (vice if the chosen captain didn't play)
    IReadOnlyList<GameweekPlayerScore> Breakdown);
