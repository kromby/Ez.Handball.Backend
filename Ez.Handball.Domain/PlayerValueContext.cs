namespace Ez.Handball.Domain;

public sealed record PlayerValueContext(
    string? Season,          // null/blank => resolve current season
    string? TournamentId,    // optional narrower scope
    int? RuleSetVersion,     // null => the flavor's default version
    string? Phase,           // RESERVED — accepted, ignored in v1
    DateOnly? SnapshotDate); // RESERVED — accepted, ignored in v1
