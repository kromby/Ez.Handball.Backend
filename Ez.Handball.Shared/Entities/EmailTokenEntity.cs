using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public sealed class EmailTokenEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // "verify" | "reset"
    public string RowKey { get; set; } = string.Empty;       // hex SHA-256 of the token secret
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
