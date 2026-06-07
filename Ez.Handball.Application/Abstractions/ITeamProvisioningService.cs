using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ITeamProvisioningService
{
    // Idempotent: if the (user, flavor) team does not exist, create it + a budget seeded to the
    // constraints' StartingCap. Does nothing if the team already exists.
    Task ProvisionAsync(string userId, GameFlavor flavor, string teamName, CancellationToken ct);
}
