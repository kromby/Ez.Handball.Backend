using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public sealed class UserEmailIndexEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "email";
    public string RowKey { get; set; } = string.Empty;   // normalized email
    public string UserId { get; set; } = string.Empty;
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
