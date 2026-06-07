using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SellPlayerResult
{
    public sealed record Sold(SquadView View) : SellPlayerResult;                 // 200
    public sealed record NotInSquad : SellPlayerResult { public static readonly NotInSquad Instance = new(); }       // 404
    public sealed record RuleSetNotFound : SellPlayerResult { public static readonly RuleSetNotFound Instance = new(); } // 400
    public sealed record NoTeam : SellPlayerResult { public static readonly NoTeam Instance = new(); }               // 409
}

public interface ISellPlayerUseCase
{
    Task<SellPlayerResult> ExecuteAsync(string userId, string playerId, BuyPlayerContext context, CancellationToken ct);
}

public sealed class SellPlayerUseCase : ISellPlayerUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGetSquadUseCase _squadView;
    private readonly IPlayerSalaryService _salary;
    private readonly ISquadConstraintsRepository _constraints;
    private readonly IGameTeamRepository _teams;
    private readonly IGameRosterRepository _roster;
    private readonly IGameBudgetRepository _budget;
    private readonly Func<DateTimeOffset> _now;

    public SellPlayerUseCase(
        IGetSquadUseCase squadView, IPlayerSalaryService salary, ISquadConstraintsRepository constraints,
        IGameTeamRepository teams, IGameRosterRepository roster, IGameBudgetRepository budget,
        Func<DateTimeOffset> now)
    {
        _squadView = squadView;
        _salary = salary;
        _constraints = constraints;
        _teams = teams;
        _roster = roster;
        _budget = budget;
        _now = now;
    }

    public async Task<SellPlayerResult> ExecuteAsync(
        string userId, string playerId, BuyPlayerContext context, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return SellPlayerResult.NoTeam.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var entry = await _roster.GetAsync(teamId, playerId, ct);
        if (entry is null || entry.DeletedAt is not null)
            return SellPlayerResult.NotInSquad.Instance;

        var version = context.RuleSetVersion ?? DefaultVersion;
        var salary = await _salary.GetSalaryAsync(playerId, version, context.Season, context.TournamentId, ct);
        if (salary is null) return SellPlayerResult.RuleSetNotFound.Instance;

        // Constraints are a separate versioned axis (fantasy-squad-v{n}) from the pricing
        // ruleSetVersion (fantasy-price-v{n}); v1 is the only constraints version in play.
        var constraints = await _constraints.GetAsync(DefaultVersion, ct);
        if (constraints is null) return SellPlayerResult.RuleSetNotFound.Instance;

        var credit = SellValue.Compute(entry.PricePaidAmount, salary.Cost.Amount, constraints.SellOnFeeRate);

        var now = _now();
        await _roster.SoftDeleteAsync(teamId, playerId, now, ct);
        await _budget.TryCreditAsync(teamId, credit, now, ct);

        var view = await _squadView.ExecuteAsync(userId, context.Season, context.TournamentId, context.RuleSetVersion, ct);
        return view is GetSquadResult.Found f
            ? new SellPlayerResult.Sold(f.View)
            : SellPlayerResult.RuleSetNotFound.Instance;
    }
}
