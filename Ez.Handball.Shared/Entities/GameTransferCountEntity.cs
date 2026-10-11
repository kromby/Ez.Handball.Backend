using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Buys and sells a team made for one round. PartitionKey = teamId, RowKey = roundLabel.
// Kept per team (unlike the global weekly-bucketed GameTransferLedger) so the allowance reads
// a single partition.
public sealed class GameTransferCountEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // roundLabel
    public int Buys { get; set; }
    public int Sells { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
