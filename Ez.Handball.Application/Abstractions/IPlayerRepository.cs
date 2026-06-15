using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(string playerId, CancellationToken ct);

    // All non-retired players whose current ClubId matches. Empty when none.
    Task<IReadOnlyList<Player>> ListByClubAsync(string clubId, CancellationToken ct);
}
