using Ez.Handball.Api.Models;

namespace Ez.Handball.Api.Services;

public interface IPlayerStatsService
{
    Task<IReadOnlyList<PlayerStatRow>> GetStatsAsync(string playerId, CancellationToken ct = default);
}
