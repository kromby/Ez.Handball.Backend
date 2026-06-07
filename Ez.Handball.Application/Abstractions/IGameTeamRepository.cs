using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IGameTeamRepository
{
    // True if the user already has a team for this flavor.
    Task<bool> ExistsAsync(string userId, GameFlavor flavor, CancellationToken ct);

    // Create the team header (idempotent upsert). Does NOT seed the budget — the caller
    // (TeamProvisioningService) creates the budget row separately.
    Task CreateAsync(string userId, GameFlavor flavor, string name, string color,
        DateTimeOffset createdAt, CancellationToken ct);

    // Read the team header, or null if the user has no team for this flavor.
    Task<GameTeam?> GetAsync(string userId, GameFlavor flavor, CancellationToken ct);

    // Update only the team name (preserves color + createdAt).
    Task RenameAsync(string userId, GameFlavor flavor, string newName, CancellationToken ct);
}
