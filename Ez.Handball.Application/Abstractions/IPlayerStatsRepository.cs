using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerStatsRepository
{
    Task<IReadOnlyList<PlayerStat>> GetByPlayerAsync(string playerId, CancellationToken ct);

    // All player stat rows for one match (PlayerStats PartitionKey = matchId).
    Task<IReadOnlyList<PlayerStat>> GetByMatchAsync(string matchId, CancellationToken ct);
}
