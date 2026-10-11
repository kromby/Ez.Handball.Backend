using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetSquadResult
{
    public sealed record RuleSetNotFound : GetSquadResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record Found(SquadView View) : GetSquadResult;
}

public interface IGetSquadUseCase
{
    Task<GetSquadResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct);
}

public sealed class GetSquadUseCase : IGetSquadUseCase
{
    private const int DefaultVersion = 1;

    private readonly ISquadRepository _squad;
    private readonly IPlayerRepository _players;
    private readonly IPlayerPriceService _price;
    private readonly ISquadConstraintsRepository _constraints;

    public GetSquadUseCase(
        ISquadRepository squad, IPlayerRepository players, IPlayerPriceService price,
        ISquadConstraintsRepository constraints)
    {
        _constraints = constraints;
        _squad = squad;
        _players = players;
        _price = price;
    }

    public async Task<GetSquadResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct)
    {
        var squad = await _squad.GetAsync(userId, GameFlavor.Fantasy, ct);
        var version = ruleSetVersion ?? DefaultVersion;

        var items = new List<SquadPlayer>(squad.Players.Count);
        double squadValue = 0;
        double budgetUsed = 0;

        foreach (var slot in squad.Players)
        {
            var price = await _price.GetPriceAsync(slot.PlayerId, version, season, tournamentId, ct);
            // The price rule set is shared across all players, so a null means the version
            // doesn't exist at all — this fires on the first slot if the rule set is missing.
            if (price is null) return GetSquadResult.RuleSetNotFound.Instance;

            var player = await _players.GetByIdAsync(slot.PlayerId, ct);
            items.Add(new SquadPlayer(
                PlayerId: slot.PlayerId,
                Name: player?.Name,
                ClubId: player?.ClubId,
                ClubName: player?.ClubName,
                Position: player?.Position,
                Gender: player?.Gender,
                Price: price.Price,
                PricePaid: slot.PricePaid,
                Rating: price.Rating,
                PositionSecondary: player?.PositionSecondary));

            squadValue += price.Price.Amount;
            budgetUsed += slot.PricePaid.Amount;
        }

        items = await WithAssignedPositionsAsync(items, ct);

        var view = new SquadView(
            items,
            BudgetUsed: new PlayerPrice(budgetUsed, squad.Currency),
            RemainingBudget: new PlayerPrice(squad.Budget, squad.Currency),
            SquadValue: new PlayerPrice(squadValue, squad.Currency));
        return new GetSquadResult.Found(view);
    }

    // #108: the slot each player fills under the squad caps, preferring primaries; highest-rated
    // players are placed first so the result is stable. Primary when there are no caps or the squad
    // can't be arranged (over a cap after a live position change).
    private async Task<List<SquadPlayer>> WithAssignedPositionsAsync(List<SquadPlayer> items, CancellationToken ct)
    {
        var constraints = await _constraints.GetAsync(DefaultVersion, ct);
        var ordered = items.OrderByDescending(p => p.Rating).ThenBy(p => p.PlayerId, StringComparer.Ordinal).ToList();
        var assignment = constraints is null
            ? null
            : PositionAssignment.TrySolve(
                ordered.Select(p => new PositionCandidate(p.PlayerId, p.EligiblePositions)).ToList(),
                PositionAssignment.MaxOnly(constraints.PositionLimits));

        return items.Select(p => p with
        {
            AssignedPosition = assignment is not null && assignment.TryGetValue(p.PlayerId, out var pos) ? pos : p.Position
        }).ToList();
    }
}
