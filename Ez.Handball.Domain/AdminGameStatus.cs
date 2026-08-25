namespace Ez.Handball.Domain;

// Admin-only cross-check of the hsi.is schedule against what has actually been
// ingested into the Matches table — surfaces games the pipeline missed.
public sealed record AdminGameStatus(
    string MatchId,
    DateTimeOffset Date,
    string? Venue,
    string HomeTeamName,
    string AwayTeamName,
    string Status,     // "played" | "upcoming"
    bool Ingested,
    bool HbStatzIngested);

public sealed record AdminRoundGames(
    string Round,
    IReadOnlyList<AdminGameStatus> Games);

public sealed record AdminTournamentGames(
    string TournamentId,
    string Name,
    string CompetitionName,
    DateTimeOffset? LastSyncedAt,
    IReadOnlyList<AdminRoundGames> Rounds);
