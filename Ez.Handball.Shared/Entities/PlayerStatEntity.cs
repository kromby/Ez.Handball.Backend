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
    public string TournamentId { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string? ClubName { get; set; }

    // HBStatz enrichment — all nullable, absent until TriggerHbStatzSyncFunction merges them
    // in. Written via a fetch-then-merge (never a bare partial upsert): a plain non-nullable
    // int on a partial entity would serialize as 0 and, under TableUpdateMode.Merge, clobber
    // the real HSÍ-sourced value above. Nullable columns are omitted by the SDK when null,
    // so leaving these unset never overwrites anything.
    public int? HbStatzAssists { get; set; }
    public int? HbStatzTurnovers { get; set; }
    public int? HbStatzSteals { get; set; }
    public int? HbStatzBlocks { get; set; }
    public int? HbStatzLegalStops { get; set; }
    public int? HbStatzShots { get; set; }
    public double? HbStatzExpectedGoals { get; set; }
    public int? HbStatzSaves { get; set; }
    public int? HbStatzShotsFaced { get; set; }
    public double? HbStatzSavePct { get; set; }
    public double? HbStatzExpectedSaves { get; set; }
    public double? HbStatzGradeTotal { get; set; }
    public double? HbStatzGradeOffense { get; set; }
    public double? HbStatzGradeDefense { get; set; }
    public double? HbStatzGradeGoalkeeping { get; set; }
}
