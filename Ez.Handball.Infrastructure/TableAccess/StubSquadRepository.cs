using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Infrastructure.TableAccess;

// Stub squad read for #53: a brand-new manager with an empty squad and the starting cap
// as their cash balance. #54 replaces this with a real Table-backed squad.
internal sealed class StubSquadRepository : ISquadRepository
{
    private const int ConstraintsVersion = 1;

    private readonly ISquadConstraintsRepository _constraints;

    public StubSquadRepository(ISquadConstraintsRepository constraints) => _constraints = constraints;

    public async Task<Squad> GetAsync(string userId, GameFlavor flavor, CancellationToken ct)
    {
        var constraints = await _constraints.GetAsync(ConstraintsVersion, ct);
        var budget = constraints?.StartingCap ?? 0;
        var currency = constraints?.Currency ?? "ISK";
        return new Squad(System.Array.Empty<SquadSlot>(), budget, currency);
    }
}
