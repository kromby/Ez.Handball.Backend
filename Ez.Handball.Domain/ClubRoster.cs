namespace Ez.Handball.Domain;

// Resource for GET /api/clubs/{clubId}/roster: the club's current-season player
// pool (same selection and per-player data as GET /api/players?clubId=), so
// retired players and players who have since moved clubs drop out.
public sealed record ClubRoster(
    string ClubId,
    string? Season,
    IReadOnlyList<ClubRosterPlayer> Players);

// A player-pool entry plus the roster-only fields from the Players table.
public sealed record ClubRosterPlayer : PlayerPoolEntry
{
    public ClubRosterPlayer(PlayerPoolEntry entry, string? jerseyNumber, int? age) : base(entry)
    {
        JerseyNumber = jerseyNumber;
        Age = age;
    }

    public string? JerseyNumber { get; init; }
    public int? Age { get; init; }
}
