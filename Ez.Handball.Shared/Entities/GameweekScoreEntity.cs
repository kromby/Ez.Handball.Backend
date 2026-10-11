using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One settled score. PartitionKey = teamId, RowKey = roundLabel. BreakdownJson is the serialized
// per-player breakdown (opaque to storage; deserialized by the repository). Points is net of
// TransferHit.
public sealed class GameweekScoreEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // roundLabel
    public double Points { get; set; }
    public string? CaptainPlayerId { get; set; }
    public string BreakdownJson { get; set; } = "[]";
    public int TransferHit { get; set; } // 0 on rows settled before #171
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
