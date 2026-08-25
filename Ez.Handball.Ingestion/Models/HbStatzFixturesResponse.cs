using System.Text.Json.Serialization;

namespace Ez.Handball.Ingestion.Models;

// GET https://hbstatz.is/api/league.php?comp={comp}&gender={M|F}&season={startYear}&view=fixtures
// Only the fields the matcher needs are modelled — the response also carries `meta`/`filters`
// blocks we don't use.
public class HbStatzFixturesResponse
{
    [JsonPropertyName("fixtures")]
    public List<HbStatzFixture> Fixtures { get; set; } = new();
}

public class HbStatzFixture
{
    [JsonPropertyName("game_id")]
    public int GameId { get; set; }

    // "yyyy-MM-dd HH:mm:ss", no timezone — same Iceland-has-no-DST assumption as the rest
    // of the pipeline (local wall-clock time is numerically UTC).
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("home")]
    public HbStatzFixtureTeam Home { get; set; } = new();

    [JsonPropertyName("away")]
    public HbStatzFixtureTeam Away { get; set; } = new();

    [JsonPropertyName("played")]
    public bool Played { get; set; }

    // Only fixtures with has_hbs actually have a full report at /api/game.php.
    [JsonPropertyName("has_hbs")]
    public bool HasHbs { get; set; }
}

public class HbStatzFixtureTeam
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
