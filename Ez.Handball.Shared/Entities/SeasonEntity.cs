using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class SeasonEntity : ITableEntity
{
    // PartitionKey always "season"
    public string PartitionKey { get; set; } = "season";
    // RowKey = season label, e.g. "2025-26"
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Season start year, e.g. 2025. Drives newest-first ordering and the
    // seed-time "newest season" guard.
    public int StartYear { get; set; }

    // Exactly one season row is current once any season has been seeded.
    public bool IsCurrent { get; set; }
}
