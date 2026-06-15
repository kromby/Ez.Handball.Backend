using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IClubRepository
{
    Task<bool> ExistsAsync(string clubId, CancellationToken ct);

    // Single club by id, or null when the id is not in the Clubs table.
    Task<Club?> GetByIdAsync(string clubId, CancellationToken ct);

    Task<IReadOnlyList<Club>> ListAsync(CancellationToken ct);
}
