using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class PlayerPositionObservationEntity : ITableEntity
{
    // PartitionKey = hsi.is playerId; RowKey = matchId — one row per (player, match) HBStatz
    // observation, so re-processing the same match is a plain idempotent upsert (no double-count).
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Position { get; set; } = string.Empty; // mapped code, e.g. "LB"
    public DateTimeOffset MatchDate { get; set; }
}
