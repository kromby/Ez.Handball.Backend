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
    DateTimeOffset CreatedAt);

public sealed record ShortlistView(
    IReadOnlyList<ShortlistPlayer> Items,
    int Count,
    int Max);
