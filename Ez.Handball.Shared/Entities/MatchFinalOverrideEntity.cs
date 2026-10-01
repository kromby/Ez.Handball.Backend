using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// An admin's "treat this match as final" override (Backend#147), for a played match hsi.is never
// marks "S". Kept out of Matches because ingestion rewrites those rows in Replace mode.
// PartitionKey = tournamentId, RowKey = matchId.
public sealed class MatchFinalOverrideEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // tournamentId
    public string RowKey { get; set; } = string.Empty;       // matchId
    public string SetBy { get; set; } = string.Empty;        // admin userId
    public DateTimeOffset SetAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
