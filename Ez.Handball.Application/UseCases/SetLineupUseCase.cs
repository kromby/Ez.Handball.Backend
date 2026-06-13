using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SetLineupResult
{
    public sealed record NoTeam : SetLineupResult { public static readonly NoTeam Instance = new(); }
    public sealed record RuleSetNotFound : SetLineupResult { public static readonly RuleSetNotFound Instance = new(); }
    public sealed record Rejected(IReadOnlyList<LineupViolation> Violations) : SetLineupResult;
    public sealed record Committed(LineupView View) : SetLineupResult;
}

public interface ISetLineupUseCase
{
    Task<SetLineupResult> ExecuteAsync(
        string userId, Lineup proposed, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct);
}

public sealed class SetLineupUseCase : ISetLineupUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGetSquadUseCase _squad;
    private readonly ILineupRepository _lineup;
    private readonly ILineupConstraintsRepository _constraints;
    private readonly IGameweekSnapshotGuard _guard;

    public SetLineupUseCase(
        IGameTeamRepository teams, IGetSquadUseCase squad,
        ILineupRepository lineup, ILineupConstraintsRepository constraints,
        IGameweekSnapshotGuard guard)
    {
        _teams = teams;
        _squad = squad;
        _lineup = lineup;
        _constraints = constraints;
        _guard = guard;
    }

    public async Task<SetLineupResult> ExecuteAsync(
        string userId, Lineup proposed, string? season, string? tournamentId, int? ruleSetVersion, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return SetLineupResult.NoTeam.Instance;

        await _guard.EnsureSnapshotsAsync(GameTeamId.For(userId, GameFlavor.Fantasy), null, ct);

        var version = ruleSetVersion ?? DefaultVersion;
        var constraints = await _constraints.GetAsync(version, ct);
        if (constraints is null) return SetLineupResult.RuleSetNotFound.Instance;

        var squadResult = await _squad.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
        if (squadResult is not GetSquadResult.Found found)
            return SetLineupResult.RuleSetNotFound.Instance;

        var validation = LineupValidator.Validate(proposed, found.View.Players, constraints);
        if (!validation.IsValid)
            return new SetLineupResult.Rejected(validation.Violations);

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        await _lineup.ReplaceAsync(teamId, proposed, ct);

        var view = LineupViewMapper.Map(found.View.Players, proposed, constraints, validation);
        return new SetLineupResult.Committed(view);
    }
}
