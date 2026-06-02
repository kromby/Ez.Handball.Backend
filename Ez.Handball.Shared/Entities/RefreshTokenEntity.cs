using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public sealed class RefreshTokenEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // hex SHA-256 of the secret
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
