using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ILineupConstraintsRepository
{
    // Reads the fantasy-lineup-v{version} config group; null if it doesn't exist.
    Task<LineupConstraints?> GetAsync(int version, CancellationToken ct);
}
