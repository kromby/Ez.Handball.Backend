using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One placed player in a team's current lineup. PartitionKey = teamId, RowKey = playerId.
// No soft-delete: a lineup is a complete snapshot wholly replaced on each save.
public sealed class GameLineupEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // playerId
    public string Role { get; set; } = string.Empty;         // "Bench" | "Starter" | "Captain" | "Vice"
    public int? BenchOrder { get; set; }                     // set iff Role == "Bench"
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
