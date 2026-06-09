using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One enabled (type, channel) cell. PartitionKey = userId, RowKey = "{Type}:{Channel}".
// A row's existence means the cell is enabled; there are no rows for disabled cells.
public sealed class NotificationPreferenceEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // "{Type}:{Channel}"
    public string Type { get; set; } = string.Empty;         // NotificationType name
    public string Channel { get; set; } = string.Empty;      // NotificationChannel name
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
