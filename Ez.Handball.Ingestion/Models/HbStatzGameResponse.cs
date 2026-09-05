using System.Text.Json.Serialization;

namespace Ez.Handball.Ingestion.Models;

// GET https://hbstatz.is/api/game.php?id={gameId}
// The real payload also carries meta/scoreline/team_totals/shot_chart/timeline/discipline/etc.
// — this only models the flat per-player lines we currently persist (System.Text.Json ignores
// unmapped properties, so the rest is simply not deserialized here).
public class HbStatzGameResponse
{
    [JsonPropertyName("players")]
    public HbStatzGamePlayers? Players { get; set; }
}

public class HbStatzGamePlayers
{
    [JsonPropertyName("home")]
    public List<HbStatzPlayerLine> Home { get; set; } = new();

    [JsonPropertyName("away")]
    public List<HbStatzPlayerLine> Away { get; set; } = new();
}

public class HbStatzPlayerLine
{
    [JsonPropertyName("player_id")]
    public int PlayerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public string Position { get; set; } = string.Empty;

    [JsonPropertyName("position_secondary")]
    public string? PositionSecondary { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("is_goalkeeper")]
    public bool IsGoalkeeper { get; set; }

    [JsonPropertyName("shots")]
    public int Shots { get; set; }

    [JsonPropertyName("assists")]
    public int Assists { get; set; }

    [JsonPropertyName("turnovers")]
    public int Turnovers { get; set; }

    [JsonPropertyName("steals")]
    public int Steals { get; set; }

    [JsonPropertyName("blocks")]
    public int Blocks { get; set; }

    [JsonPropertyName("legal_stops")]
    public int LegalStops { get; set; }

    [JsonPropertyName("xg")]
    public double? Xg { get; set; }

    [JsonPropertyName("gk_saves")]
    public int GkSaves { get; set; }

    [JsonPropertyName("gk_shots_faced")]
    public int GkShotsFaced { get; set; }

    [JsonPropertyName("gk_save_pct")]
    public double? GkSavePct { get; set; }

    [JsonPropertyName("gk_xs")]
    public double? GkXs { get; set; }

    [JsonPropertyName("grade_total")]
    public double? GradeTotal { get; set; }

    [JsonPropertyName("grade_offense")]
    public double? GradeOffense { get; set; }

    [JsonPropertyName("grade_defense")]
    public double? GradeDefense { get; set; }

    [JsonPropertyName("grade_goalkeeping")]
    public double? GradeGoalkeeping { get; set; }
}
