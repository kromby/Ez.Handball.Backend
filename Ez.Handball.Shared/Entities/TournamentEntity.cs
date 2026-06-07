using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public class TournamentEntity : ITableEntity
{
    // PartitionKey = season label, e.g. "2025-26"
    public string PartitionKey { get; set; } = string.Empty;
    // RowKey = tournamentId from hsi.is
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    // "karlar" or "kvenna"
    public string Gender { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    // "league" | "playoffs" | "cup"
    public string Type { get; set; } = string.Empty;
    // Stable season-independent competition slug, e.g. "olis-karla".
    public string CompetitionId { get; set; } = string.Empty;
    // Display name, e.g. "Olís deild karla".
    public string CompetitionName { get; set; } = string.Empty;
    // Ingestion pipeline retrieves match data for this tournament.
    public bool Ingest { get; set; }
    // Surfaced/selectable in the UI (the /api/tournaments display list).
    public bool Active { get; set; }
    public int Priority { get; set; }
}
