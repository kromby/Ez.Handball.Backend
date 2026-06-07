using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;

namespace Ez.Handball.Infrastructure.TableAccess;

// The user's fantasy squad: active owned players + a derived cash balance.
// Balance = StartingCap - sum of prices paid, so Squad.Budget stays correct for the
// #53 buy decision without any change there. Fantasy-only; the flavor is ignored.
internal sealed class TableSquadRepository : ISquadRepository
{
    private const int ConstraintsVersion = 1;

    private readonly ITableQuery _query;
    private readonly ISquadConstraintsRepository _constraints;

    public TableSquadRepository(ITableQuery query, ISquadConstraintsRepository constraints)
    {
        _query = query;
        _constraints = constraints;
    }

    public async Task<Squad> GetAsync(string userId, GameFlavor flavor, CancellationToken ct)
    {
        var slots = new List<SquadSlot>();
        await foreach (var e in _query.QueryAsync<SquadEntryEntity>(
                           Tables.Squads, $"PartitionKey eq '{ODataFilter.Escape(userId)}'", ct))
        {
            if (e.DeletedAt is null)
                slots.Add(new SquadSlot(
                    e.RowKey, e.Position, new PlayerCost(e.PricePaidAmount, e.PricePaidCurrency)));
        }

        var constraints = await _constraints.GetAsync(ConstraintsVersion, ct);
        var startingCap = constraints?.StartingCap ?? 0;
        var currency = constraints?.Currency ?? "ISK";
        var budget = startingCap - slots.Sum(s => s.PricePaid.Amount);

        return new Squad(slots, budget, currency);
    }
}
