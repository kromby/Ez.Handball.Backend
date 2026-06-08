using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// A mini-league header. PartitionKey = "league" (constant), RowKey = leagueId.
public sealed class MiniLeagueEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // "league"
    public string RowKey { get; set; } = string.Empty;       // leagueId
    public string Name { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;       // season label, e.g. "2025-26"
    public string CreatorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
