using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
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
    private readonly IPlayerPriceService _price;
    private readonly ISquadConstraintsRepository _constraints;
    private readonly IGameTeamRepository _teams;
    private readonly IGameRosterRepository _roster;
    private readonly IGameBudgetRepository _budget;
    private readonly Func<DateTimeOffset> _now;
    private readonly IGameweekSnapshotGuard _guard;

    public SellPlayerUseCase(
        IGetSquadUseCase squadView, IPlayerPriceService price, ISquadConstraintsRepository constraints,
        IGameTeamRepository teams, IGameRosterRepository roster, IGameBudgetRepository budget,
        Func<DateTimeOffset> now, IGameweekSnapshotGuard guard)
    {
        _squadView = squadView;
        _price = price;
        _constraints = constraints;
        _teams = teams;
        _roster = roster;
        _budget = budget;
        _now = now;
        _guard = guard;
    }

    public async Task<SellPlayerResult> ExecuteAsync(
        string userId, string playerId, BuyPlayerContext context, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return SellPlayerResult.NoTeam.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        await _guard.EnsureSnapshotsAsync(teamId, null, ct);
        var entry = await _roster.GetAsync(teamId, playerId, ct);
        if (entry is null || entry.DeletedAt is not null)
            return SellPlayerResult.NotInSquad.Instance;

        var version = context.RuleSetVersion ?? DefaultVersion;
        var price = await _price.GetPriceAsync(playerId, version, context.Season, context.TournamentId, ct);
        if (price is null) return SellPlayerResult.RuleSetNotFound.Instance;

        // Constraints are a separate versioned axis (fantasy-squad-v{n}) from the pricing
        // ruleSetVersion (fantasy-price-v{n}); v1 is the only constraints version in play.
        var constraints = await _constraints.GetAsync(DefaultVersion, ct);
        if (constraints is null) return SellPlayerResult.RuleSetNotFound.Instance;

        var credit = SellValue.Compute(entry.PricePaidAmount, price.Price.Amount, constraints.SellOnFeeRate);

        var now = _now();
        await _roster.SoftDeleteAsync(teamId, playerId, now, ct);
        await _budget.TryCreditAsync(teamId, credit, now, ct);

        var view = await _squadView.ExecuteAsync(userId, context.Season, context.TournamentId, context.RuleSetVersion, ct);
        return view is GetSquadResult.Found f
            ? new SellPlayerResult.Sold(f.View)
            : SellPlayerResult.RuleSetNotFound.Instance;
    }
}
