using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// One owned player in a user's fantasy squad. Mirrors ShortlistEntryEntity, including
// soft-delete (DeletedAt) so the upcoming sell/mutation issue has a deletion path; the
// read filters to active rows (DeletedAt is null).
public sealed class SquadEntryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // userId
    public string RowKey { get; set; } = string.Empty;       // playerId
    public string? Position { get; set; }                    // position snapshot (buy decision position-limit rule)
    public double PricePaidAmount { get; set; }              // price locked at buy time
    public string PricePaidCurrency { get; set; } = "ISK";   // currency of PricePaid
    public DateTimeOffset CreatedAt { get; set; }            // when the player was acquired
    public DateTimeOffset? DeletedAt { get; set; }           // null while owned
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
