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
    private readonly IPlayerSalaryService _salary;
    private readonly ISquadConstraintsRepository _constraints;
    private readonly ISquadRepository _squad;

    public GetBuyDecisionUseCase(
        IEnumerable<IBuyPlayerFunction> functions,
        IPlayerRepository players,
        IPlayerSalaryService salary,
        ISquadConstraintsRepository constraints,
        ISquadRepository squad)
    {
        _functions = functions.ToDictionary(f => f.Flavor);
        _players = players;
        _salary = salary;
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

        // Manager is a stub: no salary/constraints/squad I/O — it ignores all but PlayerId.
        if (flavor == GameFlavor.Manager)
        {
            var stubInputs = new BuyPlayerInputs(
                playerId, player.Position, new PlayerCost(0, "ISK"), "manager-v0",
                new SquadConstraints(0, 0, new Dictionary<string, int>(), 0, "ISK"),
                new Squad(System.Array.Empty<SquadSlot>(), 0, "ISK"), context);
            return new BuyDecisionResult.Decided(function.Evaluate(stubInputs));
        }

        var version = context.RuleSetVersion ?? DefaultVersion;
        var salary = await _salary.GetSalaryAsync(playerId, version, context.Season, context.TournamentId, ct);
        if (salary is null) return BuyDecisionResult.RuleSetNotFound.Instance;

        var constraints = await _constraints.GetAsync(DefaultVersion, ct);
        if (constraints is null) return BuyDecisionResult.RuleSetNotFound.Instance;

        var squad = await _squad.GetAsync(userId, flavor, ct);

        var inputs = new BuyPlayerInputs(
            playerId, player.Position, salary.Cost, salary.Version, constraints, squad, context);
        return new BuyDecisionResult.Decided(function.Evaluate(inputs));
    }
}
