using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class TeamEntity : ITableEntity
{
    // PartitionKey always "team"
    public string PartitionKey { get; set; } = "team";
    // RowKey = "{clubId}-{gender}", e.g. "123-karlar"
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ClubId { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
