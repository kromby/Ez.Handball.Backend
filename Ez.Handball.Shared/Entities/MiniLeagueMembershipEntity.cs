using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Reverse-index row for "which leagues is this user in". PartitionKey = userId, RowKey = leagueId.
public sealed class MiniLeagueMembershipEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // leagueId
    public string Role { get; set; } = string.Empty;         // "creator" | "member"
    public DateTimeOffset JoinedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
