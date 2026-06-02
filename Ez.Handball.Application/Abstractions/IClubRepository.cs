using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IClubRepository
{
    Task<bool> ExistsAsync(string clubId, CancellationToken ct);

    Task<IReadOnlyList<Club>> ListAsync(CancellationToken ct);
}
