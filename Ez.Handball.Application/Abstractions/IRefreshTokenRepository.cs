using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Application.Abstractions;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshTokenEntity token, CancellationToken ct);
    Task<RefreshTokenEntity?> GetAsync(string userId, string secretHash, CancellationToken ct);
    Task DeleteAsync(string userId, string secretHash, CancellationToken ct);
    Task DeleteAllForUserAsync(string userId, CancellationToken ct);
}
