using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One owned player on a team's roster. PartitionKey = teamId, RowKey = playerId. Soft-delete
// via DeletedAt (null while owned). Currency is not stored; it comes from the constraints.
public sealed class GameRosterEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // playerId
    public string? Position { get; set; }                    // position snapshot
    public double PricePaidAmount { get; set; }              // price locked at buy time
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }           // null while owned
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
