namespace Ez.Handball.Domain;

// Resource for GET /api/clubs/{clubId}/roster. Season is informational (the roster
// is the club's current members, not season-filtered).
public sealed record ClubRoster(
    string ClubId,
    string? Season,
    IReadOnlyList<ClubRosterPlayer> Players);

public sealed record ClubRosterPlayer(
    string PlayerId,
    string Name,
    string? JerseyNumber,
    string Position,
    int? Age);
