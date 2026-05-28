using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class TournamentEntity : ITableEntity
{
    // PartitionKey = season, e.g. "2025"
    public string PartitionKey { get; set; } = string.Empty;
    // RowKey = tournamentId from hsi.is
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    // "karlar" or "kvenna"
    public string Gender { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
}
