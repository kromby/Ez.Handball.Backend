using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerPriceService
{
    // The fantasy ISK price for the player in scope, or null if the price rule set
    // `version` or the underlying scoring rule set does not exist.
    Task<PlayerPricing?> GetPriceAsync(
        string playerId, int version, string? season, string? tournamentId, CancellationToken ct);
}
