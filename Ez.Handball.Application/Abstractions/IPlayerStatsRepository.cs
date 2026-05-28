using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerStatsRepository
{
    Task<IReadOnlyList<PlayerStat>> GetByPlayerAsync(string playerId, CancellationToken ct);
}
