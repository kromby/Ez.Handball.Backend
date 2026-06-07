using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// A user's team for one game flavor. PartitionKey = userId, RowKey = flavor (e.g. "fantasy").
public sealed class GameTeamEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // flavor, e.g. "fantasy"
    public string TeamId { get; set; } = string.Empty;       // "{userId}:{flavor}"
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
