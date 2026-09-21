namespace Ez.Handball.Domain;

// Raw repository row (active or soft-deleted).
public sealed record ShortlistEntry(
    string PlayerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

// Enriched item returned by the fetch endpoint. Price/PickPercentage are
// reserved (always null today) for a future pricing system and Pk% (#25).
// All enrichment fields are null when the player can't be resolved this season.
public sealed record ShortlistPlayer(
    string PlayerId,
    string? Name,
    string? ClubId,
    string? ClubName,
    string? Position,
    string? Gender,
    decimal? Price,
    double? PickPercentage,
    DateTimeOffset CreatedAt,
    string? PositionSecondary = null,
    // Current-season HBStatz-derived aggregates. Null when the player can't be
    // resolved this season (same condition that nulls Position/Gender above).
    int? Games = null,
    int? Goals = null,
    int? YellowCards = null,
    int? TwoMinuteSuspensions = null,
    int? RedCards = null,
    int? Assists = null,
    int? Steals = null,
    int? Blocks = null,
    int? Saves = null,
    int? Turnovers = null,
    int? LegalStops = null,
    int? Shots = null,
    double? ExpectedGoals = null,
    int? ShotsFaced = null,
    double? SavePct = null,
    double? ExpectedSaves = null,
    double? GradeTotal = null,
    double? GradeOffense = null,
    double? GradeDefense = null,
    double? GradeGoalkeeping = null);

public sealed record ShortlistView(
    IReadOnlyList<ShortlistPlayer> Items,
    int Count,
    int Max);
