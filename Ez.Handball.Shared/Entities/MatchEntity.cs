using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class MatchEntity : ITableEntity
{
    // PartitionKey = tournamentId
    public string PartitionKey { get; set; } = string.Empty;
    // RowKey = matchId
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Synthetic team IDs: "{clubId}-{gender}"
    public string HomeTeamId { get; set; } = string.Empty;
    public string AwayTeamId { get; set; } = string.Empty;
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public DateTimeOffset Date { get; set; }
    public string Status { get; set; } = string.Empty;

    public string Venue { get; set; } = string.Empty;   // PLAYING_FIELD_NAME
    public int? Attendance { get; set; }                 // GAME_SPECTATORS (nullable)
    public int HomeHalftimeScore { get; set; }           // HOME_HALFTIME_GOALS
    public int AwayHalftimeScore { get; set; }           // GUEST_HALFTIME_GOALS
    public string Round { get; set; } = string.Empty;   // HSÍ round label from the match list (e.g. "1", "Undanúrslit")
}
