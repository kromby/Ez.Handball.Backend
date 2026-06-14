namespace Ez.Handball.Domain;

// Resource for GET /api/clubs/{clubId}/matches. Current season, all competitions,
// framed from the club's perspective. Scores are null for upcoming matches.
public sealed record ClubMatchListing(
    string ClubId,
    string? Season,
    IReadOnlyList<ClubMatch> Matches);

public sealed record ClubMatch(
    string MatchId,
    string TournamentId,
    string? TournamentName,
    string Round,
    DateTimeOffset Date,
    string? Venue,
    string Status,            // "played" | "upcoming"
    bool IsHome,
    string OpponentClubId,
    string? OpponentName,
    string? OpponentLogoUrl,
    int? ClubScore,
    int? OpponentScore);
