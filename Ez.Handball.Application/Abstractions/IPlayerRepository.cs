using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(string playerId, CancellationToken ct);

    // All non-retired players whose current ClubId matches. Empty when none.
    Task<IReadOnlyList<Player>> ListByClubAsync(string clubId, CancellationToken ct);

    // Non-retired players with no Position set, or the raw scraped placeholder "Leikmaður" —
    // candidates for an admin to fix manually. Empty when none.
    Task<IReadOnlyList<Player>> ListMissingPositionAsync(CancellationToken ct);

    // Sets Position/PositionSecondary on the player's row. Returns false if no row exists for playerId.
    Task<bool> SetPositionAsync(string playerId, string position, string positionSecondary, CancellationToken ct);
}
