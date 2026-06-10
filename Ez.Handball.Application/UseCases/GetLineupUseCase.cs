using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetLineupResult
{
    public sealed record RuleSetNotFound : GetLineupResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record NotSet(double CaptainMultiplier) : GetLineupResult;
    public sealed record Found(LineupView View) : GetLineupResult;
}

public interface IGetLineupUseCase
{
    Task<GetLineupResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct);
}

public sealed class GetLineupUseCase : IGetLineupUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGetSquadUseCase _squad;
    private readonly ILineupRepository _lineup;
    private readonly ILineupConstraintsRepository _constraints;

    public GetLineupUseCase(
        IGetSquadUseCase squad, ILineupRepository lineup, ILineupConstraintsRepository constraints)
    {
        _squad = squad;
        _lineup = lineup;
        _constraints = constraints;
    }

    public async Task<GetLineupResult> ExecuteAsync(
        string userId, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct)
    {
        var version = ruleSetVersion ?? DefaultVersion;

        var constraints = await _constraints.GetAsync(version, ct);
        if (constraints is null) return GetLineupResult.RuleSetNotFound.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        var stored = await _lineup.GetAsync(teamId, ct);
        if (stored is null) return new GetLineupResult.NotSet(constraints.CaptainMultiplier);

        var squadResult = await _squad.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
        if (squadResult is not GetSquadResult.Found found)
            return GetLineupResult.RuleSetNotFound.Instance;

        var validation = LineupValidator.Validate(stored, found.View.Players, constraints);
        var view = LineupViewMapper.Map(found.View.Players, stored, constraints, validation);
        return new GetLineupResult.Found(view);
    }
}
