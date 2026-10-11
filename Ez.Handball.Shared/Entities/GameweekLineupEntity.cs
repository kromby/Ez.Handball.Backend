using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One frozen lineup slot. PartitionKey = "{teamId}|{roundLabel}", RowKey = playerId.
public sealed class GameweekLineupEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // "{teamId}|{roundLabel}"
    public string RowKey { get; set; } = string.Empty;       // playerId
    public string Role { get; set; } = string.Empty;
    public int? BenchOrder { get; set; }
    public string? PlayAs { get; set; } // position the slot was frozen at (#166); null = auto
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
