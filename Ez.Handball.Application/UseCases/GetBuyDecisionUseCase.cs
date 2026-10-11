using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.BuyFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record BuyDecisionResult
{
    public sealed record PlayerNotFound  : BuyDecisionResult { public static readonly PlayerNotFound  Instance = new(); }
    public sealed record InvalidFlavor   : BuyDecisionResult { public static readonly InvalidFlavor   Instance = new(); }
    public sealed record RuleSetNotFound : BuyDecisionResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record Decided(BuyDecision Decision) : BuyDecisionResult;
}

public interface IGetBuyDecisionUseCase
{
    Task<BuyDecisionResult> ExecuteAsync(
        string userId, string playerId, GameFlavor flavor, BuyPlayerContext context, CancellationToken ct);
}

public class GetBuyDecisionUseCase : IGetBuyDecisionUseCase
{
    private const int DefaultVersion = 1;

    private readonly IReadOnlyDictionary<GameFlavor, IBuyPlayerFunction> _functions;
    private readonly IPlayerRepository _players;
    private readonly IPlayerPriceService _price;
    private readonly ISquadConstraintsRepository _constraints;
    private readonly ISquadRepository _squad;

    public GetBuyDecisionUseCase(
        IEnumerable<IBuyPlayerFunction> functions,
        IPlayerRepository players,
        IPlayerPriceService price,
        ISquadConstraintsRepository constraints,
        ISquadRepository squad)
    {
        _functions = functions.ToDictionary(f => f.Flavor);
        _players = players;
        _price = price;
        _constraints = constraints;
        _squad = squad;
    }

    public async Task<BuyDecisionResult> ExecuteAsync(
        string userId, string playerId, GameFlavor flavor, BuyPlayerContext context, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return BuyDecisionResult.PlayerNotFound.Instance;

        if (!_functions.TryGetValue(flavor, out var function))
            return BuyDecisionResult.InvalidFlavor.Instance;

        // Manager is a stub: no price/constraints/squad I/O — it ignores all but PlayerId.
        if (flavor == GameFlavor.Manager)
        {
            var stubInputs = new BuyPlayerInputs(
                playerId, player.Position, new PlayerPrice(0, "ISK"), ManagerBuyPlayerFunction.Version,
                new SquadConstraints(0, 0, new Dictionary<string, int>(), 0, "ISK"),
                new Squad(System.Array.Empty<SquadSlot>(), 0, "ISK"), context);
            return new BuyDecisionResult.Decided(function.Evaluate(stubInputs));
        }

        var version = context.RuleSetVersion ?? DefaultVersion;
        var price = await _price.GetPriceAsync(playerId, version, context.Season, context.TournamentId, ct);
        if (price is null) return BuyDecisionResult.RuleSetNotFound.Instance;

        var constraints = await _constraints.GetAsync(DefaultVersion, ct);
        if (constraints is null) return BuyDecisionResult.RuleSetNotFound.Instance;

        var squad = await _squad.GetAsync(userId, flavor, ct);
        squad = await WithLiveEligibilityAsync(squad, ct);

        var inputs = new BuyPlayerInputs(
            playerId, player.Position, price.Price, price.Version, constraints, squad, context,
            PositionAssignment.EligibleFor(player.Position, player.PositionSecondary));
        return new BuyDecisionResult.Decided(function.Evaluate(inputs));
    }

    // #108: eligibility is live — a player's secondary can change after purchase. Falls back to
    // the buy-time snapshot when the player row no longer resolves.
    private async Task<Squad> WithLiveEligibilityAsync(Squad squad, CancellationToken ct)
    {
        var slots = new List<SquadSlot>(squad.Players.Count);
        foreach (var slot in squad.Players)
        {
            var live = await _players.GetByIdAsync(slot.PlayerId, ct);
            slots.Add(live is null
                ? slot
                : slot with { Eligible = PositionAssignment.EligibleFor(live.Position, live.PositionSecondary) });
        }
        return squad with { Players = slots };
    }
}
