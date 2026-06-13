using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Pins a gameweek's deadline the first time it locks. PartitionKey = tournamentId, RowKey = roundLabel.
public sealed class GameweekLockEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // tournamentId
    public string RowKey { get; set; } = string.Empty;       // roundLabel (gwKey)
    public DateTimeOffset PinnedDeadline { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
