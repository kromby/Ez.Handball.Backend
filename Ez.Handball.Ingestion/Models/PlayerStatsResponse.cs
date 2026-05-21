using System.Text.Json.Serialization;

namespace Ez.Handball.Ingestion.Models;

// Actual API response shape: GET /api/hsi/match/{matchId}/{clubId}/players
// Top-level: { "data": [ ... ] }  — "data" is a flat array.
// The array includes BOTH players AND non-playing staff (coaches, physios, etc.).
// Distinguish by the "PLAYER" field: "1" = player on the roster, "0" = staff.
// All stat values (goals, cards, etc.) are STRINGS in the API response.
// There is NO "minutesPlayed" field — the API tracks TWO_MINUTE_SUSPENSIONS instead.
// All field names are SCREAMING_SNAKE_CASE.

public class PlayerStatsResponse
{
    [JsonPropertyName("data")]
    public List<PlayerStatDto> Data { get; set; } = new();
}

public class PlayerStatDto
{
    [JsonPropertyName("PLAYER_ID")]
    public string PlayerId { get; set; } = string.Empty;

    [JsonPropertyName("NAME")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("POSITION")]
    public string Position { get; set; } = string.Empty;

    [JsonPropertyName("TEAM_ID")]
    public string TeamId { get; set; } = string.Empty;

    // "1" = player, "0" = staff (coach, physio, etc.) — filter to "1" when persisting
    [JsonPropertyName("PLAYER")]
    public string Player { get; set; } = string.Empty;

    [JsonPropertyName("PLAYER_JERSEY_NUMBER")]
    public string? PlayerJerseyNumber { get; set; }

    // First 6 chars are DDMMYY date of birth
    [JsonPropertyName("IDENTIFIER")]
    public string? Identifier { get; set; }

    // All stat values are returned as strings by the API — parse to int as needed
    [JsonPropertyName("GOALS")]
    public string Goals { get; set; } = "0";

    [JsonPropertyName("PENALTIES")]
    public string Penalties { get; set; } = "0";

    [JsonPropertyName("YELLOW_CARDS")]
    public string YellowCards { get; set; } = "0";

    // 2-minute suspensions (equivalent to yellow card in handball)
    [JsonPropertyName("TWO_MINUTE_SUSPENSIONS")]
    public string TwoMinuteSuspensions { get; set; } = "0";

    [JsonPropertyName("RED_CARDS")]
    public string RedCards { get; set; } = "0";
}
