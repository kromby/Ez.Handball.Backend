using Ez.Handball.Api.Models;

namespace Ez.Handball.Api.Services;

public interface IPlayerLookupService
{
    Task<PlayerProfile?> GetPlayerAsync(string playerId, CancellationToken ct = default);
}
