using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// A mini-league invite token. PartitionKey = "invite" (constant), RowKey = token (plaintext).
public sealed class MiniLeagueInviteEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;      // "invite"
    public string RowKey { get; set; } = string.Empty;            // opaque token
    public string LeagueId { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }                // null = never expires
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
