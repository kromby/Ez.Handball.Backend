using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class ClubEntity : ITableEntity
{
    // PartitionKey always "club"
    public string PartitionKey { get; set; } = "club";
    // RowKey = clubId from hsi.is
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;

    // Out-of-band club logo URL. Populated outside the ingestion pipeline; MatchParser
    // preserves it via Merge upserts. Surfaced as Club.LogoUrl.
    public string? LogoSrc { get; set; }
}
