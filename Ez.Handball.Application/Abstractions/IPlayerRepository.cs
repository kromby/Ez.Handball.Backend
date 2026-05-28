using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(string playerId, CancellationToken ct);
}
