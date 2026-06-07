using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// A team's stored cash balance. PartitionKey = teamId, RowKey = "balance" (single row).
public sealed class GameBudgetEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // teamId
    public string RowKey { get; set; } = string.Empty;       // "balance"
    public double Amount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
