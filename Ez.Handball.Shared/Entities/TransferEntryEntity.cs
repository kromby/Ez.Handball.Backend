using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Append-only ledger of a single buy/sell event. PartitionKey = "{flavor}-{ISO-week}"
// (e.g. "fantasy-2026-W24"); RowKey = "{reverseTicks}-{type}-{playerId}-{userId}" (newest
// first, unique across buy/sell/buy of the same player). Never updated, never deleted.
public sealed class TransferEntryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // "{flavor}-{ISO-week}"
    public string RowKey { get; set; } = string.Empty;       // "{reverseTicks}-{type}-{playerId}-{userId}"
    public string UserId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string Flavor { get; set; } = string.Empty;       // "fantasy"
    public string Type { get; set; } = string.Empty;         // "buy" | "sell"
    public double Cost { get; set; }
    public string? SeasonLabel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
