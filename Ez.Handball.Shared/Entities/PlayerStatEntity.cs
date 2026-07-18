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

    public int? Assists { get; set; }
    public int? Turnovers { get; set; }
    public int? Steals { get; set; }
    public double? PlusMinus { get; set; }
    public int? Shots { get; set; }
    public int? Blocks { get; set; }
    public double? Stops { get; set; }
    public double? ExpectedGoals { get; set; }
    public int? PenaltiesEarned { get; set; }
    public int? PenaltyGoals { get; set; }
    public int? Saves { get; set; }
    public double? SaveRate { get; set; }
    public double? ExpectedSaves { get; set; }
    public double? GoalsAgainst { get; set; }
    public int? PenaltySaves { get; set; }
}
