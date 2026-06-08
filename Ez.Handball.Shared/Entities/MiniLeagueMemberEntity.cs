using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One membership row. PartitionKey = leagueId, RowKey = userId.
public sealed class MiniLeagueMemberEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // leagueId
    public string RowKey { get; set; } = string.Empty;       // userId
    public string Role { get; set; } = string.Empty;         // "creator" | "member"
    public DateTimeOffset JoinedAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
