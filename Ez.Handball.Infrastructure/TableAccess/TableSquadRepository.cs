using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Infrastructure.TableAccess;

// The user's squad for a flavor: active roster slots + the stored cash balance. Currency is
// taken from the constraints (not stored per row). Fantasy-only today; teamId encodes flavor.
internal sealed class TableSquadRepository : ISquadRepository
{
    private const int ConstraintsVersion = 1;

    private readonly IGameRosterRepository _roster;
    private readonly IGameBudgetRepository _budget;
    private readonly ISquadConstraintsRepository _constraints;

    public TableSquadRepository(
        IGameRosterRepository roster, IGameBudgetRepository budget, ISquadConstraintsRepository constraints)
    {
        _roster = roster;
        _budget = budget;
        _constraints = constraints;
    }

    public async Task<Squad> GetAsync(string userId, GameFlavor flavor, CancellationToken ct)
    {
        var teamId = GameTeamId.For(userId, flavor);
        var constraints = await _constraints.GetAsync(ConstraintsVersion, ct);
        var currency = constraints?.Currency ?? "ISK";

        var entries = await _roster.ListActiveAsync(teamId, ct);
        var slots = entries
            .Select(e => new SquadSlot(e.PlayerId, e.Position, new PlayerCost(e.PricePaidAmount, currency)))
            .ToList();

        var budget = await _budget.GetBalanceAsync(teamId, ct) ?? 0;
        return new Squad(slots, budget, currency);
    }
}
