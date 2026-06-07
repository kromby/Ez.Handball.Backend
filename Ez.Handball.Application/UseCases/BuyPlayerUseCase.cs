using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record BuyPlayerResult
{
    public sealed record Committed(SquadView View) : BuyPlayerResult;
    public sealed record Rejected(IReadOnlyList<BuyRuleViolation> Violations) : BuyPlayerResult; // -> 422
    public sealed record Duplicate : BuyPlayerResult { public static readonly Duplicate Instance = new(); } // -> 409
    public sealed record PlayerNotFound : BuyPlayerResult { public static readonly PlayerNotFound Instance = new(); } // -> 404
    public sealed record RuleSetNotFound : BuyPlayerResult { public static readonly RuleSetNotFound Instance = new(); } // -> 400
    public sealed record NoTeam : BuyPlayerResult { public static readonly NoTeam Instance = new(); } // -> 409
}

public interface IBuyPlayerUseCase
{
    Task<BuyPlayerResult> ExecuteAsync(string userId, string playerId, BuyPlayerContext context, CancellationToken ct);
}

public sealed class BuyPlayerUseCase : IBuyPlayerUseCase
{
    private const string DuplicateCode = "duplicate_player";

    private readonly IGetBuyDecisionUseCase _decision;
    private readonly IGetSquadUseCase _squadView;
    private readonly IPlayerRepository _players;
    private readonly IGameTeamRepository _teams;
    private readonly IGameRosterRepository _roster;
    private readonly IGameBudgetRepository _budget;
    private readonly Func<DateTimeOffset> _now;

    public BuyPlayerUseCase(
        IGetBuyDecisionUseCase decision, IGetSquadUseCase squadView, IPlayerRepository players,
        IGameTeamRepository teams, IGameRosterRepository roster, IGameBudgetRepository budget,
        Func<DateTimeOffset> now)
    {
        _decision = decision;
        _squadView = squadView;
        _players = players;
        _teams = teams;
        _roster = roster;
        _budget = budget;
        _now = now;
    }

    public async Task<BuyPlayerResult> ExecuteAsync(
        string userId, string playerId, BuyPlayerContext context, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return BuyPlayerResult.NoTeam.Instance;

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return BuyPlayerResult.PlayerNotFound.Instance;

        var decision = await _decision.ExecuteAsync(userId, playerId, GameFlavor.Fantasy, context, ct);
        switch (decision)
        {
            case BuyDecisionResult.PlayerNotFound: return BuyPlayerResult.PlayerNotFound.Instance;
            case BuyDecisionResult.RuleSetNotFound: return BuyPlayerResult.RuleSetNotFound.Instance;
            case BuyDecisionResult.InvalidFlavor: return BuyPlayerResult.RuleSetNotFound.Instance; // fantasy fixed; defensive
            case BuyDecisionResult.Decided d when !d.Decision.Allowed:
                return d.Decision.Violations.Any(v => v.Code == DuplicateCode)
                    ? BuyPlayerResult.Duplicate.Instance
                    : new BuyPlayerResult.Rejected(d.Decision.Violations);
            case BuyDecisionResult.Decided d:
                return await CommitAsync(userId, playerId, player.Position, d.Decision, context, ct);
            default:
                return BuyPlayerResult.RuleSetNotFound.Instance;
        }
    }

    private async Task<BuyPlayerResult> CommitAsync(
        string userId, string playerId, string? position, BuyDecision decision, BuyPlayerContext context, CancellationToken ct)
    {
        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var cost = decision.Cost.Amount;

        // Write-time duplicate guard (decision-time squad may be stale).
        var existing = await _roster.GetAsync(teamId, playerId, ct);
        if (existing is { DeletedAt: null }) return BuyPlayerResult.Duplicate.Instance;

        var now = _now();
        if (!await _budget.TryDeductAsync(teamId, cost, now, ct))
            return new BuyPlayerResult.Rejected(new[] { new BuyRuleViolation("insufficient_budget", "Cost exceeds remaining budget") });

        var outcome = await _roster.AddOrResurrectAsync(teamId, playerId, position, cost, now, ct);
        if (outcome == RosterAddOutcome.AlreadyActive)
        {
            await _budget.TryCreditAsync(teamId, cost, now, ct); // compensate the deduction
            return BuyPlayerResult.Duplicate.Instance;
        }

        var view = await _squadView.ExecuteAsync(userId, context.Season, context.TournamentId, context.RuleSetVersion, ct);
        return view is GetSquadResult.Found f
            ? new BuyPlayerResult.Committed(f.View)
            : BuyPlayerResult.RuleSetNotFound.Instance;
    }
}
