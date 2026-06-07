namespace Ez.Handball.Domain;

public sealed record PlayerValueContext(
    string? Season,          // null/blank => resolve current season
    string? TournamentId,    // optional narrower scope (single tournament)
    string? CompetitionId,   // optional: aggregate across a competition's phases
    int? RuleSetVersion,     // null => the flavor's default version
    TournamentType? Type,    // optional: narrow to a phase (league/playoffs/cup)
    DateOnly? SnapshotDate); // RESERVED — accepted, ignored in v1
