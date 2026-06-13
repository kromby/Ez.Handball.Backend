using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IGameweekConfigRepository
{
    // Reads the fantasy-gameweek-v{version} Config group; null if it doesn't exist or is incomplete.
    Task<GameweekConfig?> GetAsync(int version, CancellationToken ct);
}
