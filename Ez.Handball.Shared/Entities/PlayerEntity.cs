using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class PlayerEntity : ITableEntity
{
    // PartitionKey = synthetic teamId, e.g. "123-karlar"
    public string PartitionKey { get; set; } = string.Empty;
    // RowKey = playerId from hsi.is
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? JerseyNumber { get; set; }
    public DateTimeOffset? DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string ClubId { get; set; } = string.Empty;
    public string? ClubName { get; set; }
}
