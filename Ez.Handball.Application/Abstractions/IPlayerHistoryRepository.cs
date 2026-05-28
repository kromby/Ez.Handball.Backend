using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerHistoryRepository
{
    Task<PlayerHistory> GetByPlayerAsync(string playerId, CancellationToken ct);
}
