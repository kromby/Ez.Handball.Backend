using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public sealed class ShortlistEntryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // playerId
    public DateTimeOffset CreatedAt { get; set; }            // when the entry became active
    public DateTimeOffset? DeletedAt { get; set; }           // null while active
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
