using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IPlayerSalaryService
{
    // The fantasy ISK salary for the player in scope, or null if the price rule set
    // `version` or the underlying scoring rule set does not exist.
    Task<PlayerSalary?> GetSalaryAsync(
        string playerId, int version, string? season, string? tournamentId, CancellationToken ct);
}
