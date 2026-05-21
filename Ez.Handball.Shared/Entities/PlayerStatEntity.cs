using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class PlayerStatEntity : ITableEntity
{
    // PartitionKey = matchId
    public string PartitionKey { get; set; } = string.Empty;
    // RowKey = playerId
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int Goals { get; set; }
    public int YellowCards { get; set; }
    public int TwoMinuteSuspensions { get; set; }
    public int RedCards { get; set; }
}
