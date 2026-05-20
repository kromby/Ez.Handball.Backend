using System.Text.Json.Serialization;

namespace Ez.Handball.Ingestion.Models;

// Actual API response shape: GET /api/hsi/match/{matchId}
// Top-level: { "data": { ... } }  — "data" is a single object (not an array).
// All field names are SCREAMING_SNAKE_CASE.
// Date format: "03.09.2025 - 19:30"  (dd.MM.yyyy - HH:mm) — stored as string.
// Status values: "S" (Slokið = finished), "O" (Opið = upcoming/in-progress).
// NOTE: The match list (GameId, TournamentId) is NOT repeated here — the match ID
//       must be tracked from the blob path / context, not from this response body.

public class MatchDetailsResponse
{
    [JsonPropertyName("data")]
    public MatchDetailsData? Data { get; set; }
}

public class MatchDetailsData
{
    [JsonPropertyName("TOURNAMENT_ID")]
    public string TournamentId { get; set; } = string.Empty;

    [JsonPropertyName("TOURNAMENT_SHORT_NAME")]
    public string TournamentShortName { get; set; } = string.Empty;

    // Format: "dd.MM.yyyy - HH:mm", e.g. "03.09.2025 - 19:30"
    [JsonPropertyName("DATE")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("CLUB_HOME_ID")]
    public string ClubHomeId { get; set; } = string.Empty;

    [JsonPropertyName("HOME_CLUB_NAME")]
    public string HomeClubName { get; set; } = string.Empty;

    [JsonPropertyName("CLUB_GUEST_ID")]
    public string ClubGuestId { get; set; } = string.Empty;

    [JsonPropertyName("GUEST_CLUB_NAME")]
    public string GuestClubName { get; set; } = string.Empty;

    // "S" = Slokið (finished), "O" = Opið (upcoming/in-progress)
    [JsonPropertyName("REPORT_STATUS")]
    public string ReportStatus { get; set; } = string.Empty;

    // Score values are strings in the API response — parse to int as needed
    [JsonPropertyName("GAMES_RESULT_HOME")]
    public string GamesResultHome { get; set; } = string.Empty;

    [JsonPropertyName("GAMES_RESULT_GUEST")]
    public string GamesResultGuest { get; set; } = string.Empty;

    [JsonPropertyName("HOME_HALFTIME_GOALS")]
    public string HomeHalftimeGoals { get; set; } = string.Empty;

    [JsonPropertyName("GUEST_HALFTIME_GOALS")]
    public string GuestHalftimeGoals { get; set; } = string.Empty;

    [JsonPropertyName("HOME_SECOND_HALFTIME_GOALS")]
    public string HomeSecondHalftimeGoals { get; set; } = string.Empty;

    [JsonPropertyName("GUEST_SECOND_HALFTIME_GOALS")]
    public string GuestSecondHalftimeGoals { get; set; } = string.Empty;

    [JsonPropertyName("PLAYING_FIELD_NAME")]
    public string PlayingFieldName { get; set; } = string.Empty;

    [JsonPropertyName("GAME_SPECTATORS")]
    public string? GameSpectators { get; set; }
}
