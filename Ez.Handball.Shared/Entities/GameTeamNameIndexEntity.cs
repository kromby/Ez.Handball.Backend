using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Team-name uniqueness index. Mirrors UserEmailIndexEntity. RowKey = normalized team name.
public sealed class GameTeamNameIndexEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "name";
    public string RowKey { get; set; } = string.Empty;   // normalized team name (trim + lowercase)
    public string TeamId { get; set; } = string.Empty;   // "{userId}:{flavor}"
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
