using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ISquadRepository
{
    // The user's current squad for a flavor: owned players + stored cash balance + currency.
    Task<Squad> GetAsync(string userId, GameFlavor flavor, CancellationToken ct);
}
