using System.Text.Json.Serialization;

namespace Ez.Handball.Ingestion.Models;

// Actual API response shape: GET /api/hsi/tournaments/{id}/matches
// Top-level: { "data": [ ... ] }  — "data" is a flat array of match objects.
// Status values observed: "S" (Slokið = finished), "O" (Opið = upcoming/in-progress).
// Date is an ISO 8601 string without timezone ("2025-09-03T19:30:00").
// Note: the API has a casing inconsistency — home team ID field is "HomeTeamid"
//       (lowercase 'd') while away team ID is "AwayTeamId" (uppercase 'D').

public class MatchListResponse
{
    [JsonPropertyName("data")]
    public List<MatchSummary> Data { get; set; } = new();
}

public class MatchSummary
{
    [JsonPropertyName("GameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonPropertyName("TournamentId")]
    public string TournamentId { get; set; } = string.Empty;

    [JsonPropertyName("TournamentName")]
    public string TournamentName { get; set; } = string.Empty;

    [JsonPropertyName("Round")]
    public string Round { get; set; } = string.Empty;

    // ISO 8601 local time string, e.g. "2025-09-03T19:30:00" — no timezone offset
    [JsonPropertyName("GameDayTime")]
    public string GameDayTime { get; set; } = string.Empty;

    // Note: "HomeTeamid" (lowercase 'd') is the actual field name returned by the API
    [JsonPropertyName("HomeTeamid")]
    public string HomeTeamId { get; set; } = string.Empty;

    [JsonPropertyName("HomeTeamName")]
    public string HomeTeamName { get; set; } = string.Empty;

    [JsonPropertyName("AwayTeamId")]
    public string AwayTeamId { get; set; } = string.Empty;

    [JsonPropertyName("AwayTeamName")]
    public string AwayTeamName { get; set; } = string.Empty;

    // "S" = Slokið (finished), "O" = Opið (upcoming/in-progress)
    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("ResultHomeTeam")]
    public string ResultHomeTeam { get; set; } = string.Empty;

    [JsonPropertyName("ResultAwayTeam")]
    public string ResultAwayTeam { get; set; } = string.Empty;

    [JsonPropertyName("ResultHomeFirstHalf")]
    public string ResultHomeFirstHalf { get; set; } = string.Empty;

    [JsonPropertyName("ResultAwayFirstHalf")]
    public string ResultAwayFirstHalf { get; set; } = string.Empty;

    [JsonPropertyName("StadiumName")]
    public string StadiumName { get; set; } = string.Empty;
}
