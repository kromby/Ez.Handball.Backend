using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Application.UseCases;

public abstract record SetPlayAsResult
{
    public sealed record Updated(SquadView View) : SetPlayAsResult;                                                   // 200
    public sealed record NoTeam : SetPlayAsResult { public static readonly NoTeam Instance = new(); }                 // 409 no_team
    public sealed record NotInSquad : SetPlayAsResult { public static readonly NotInSquad Instance = new(); }         // 404 not_in_squad
    public sealed record NotEligible : SetPlayAsResult { public static readonly NotEligible Instance = new(); }       // 422 position_not_eligible
    public sealed record Rejected(IReadOnlyList<LineupViolation> Violations) : SetPlayAsResult;                       // 422 lineup_invalid
    public sealed record Conflict : SetPlayAsResult { public static readonly Conflict Instance = new(); }             // 409 conflict
    public sealed record RuleSetNotFound : SetPlayAsResult { public static readonly RuleSetNotFound Instance = new(); } // 400 invalid_rule_set
}

public interface ISetPlayAsUseCase
{
    // position null = clear the pin.
    Task<SetPlayAsResult> ExecuteAsync(string userId, string playerId, string? position, BuyPlayerContext context, CancellationToken ct);
}

public sealed class SetPlayAsUseCase : ISetPlayAsUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameTeamRepository _teams;
    private readonly IGameweekSnapshotGuard _guard;
    private readonly IGetSquadUseCase _squadView;
    private readonly ILineupRepository _lineup;
    private readonly ILineupConstraintsRepository _constraints;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameRosterRepository _roster;
    private readonly ILogger<SetPlayAsUseCase> _logger;

    public SetPlayAsUseCase(
        IGameTeamRepository teams, IGameweekSnapshotGuard guard, IGetSquadUseCase squadView,
        ILineupRepository lineup, ILineupConstraintsRepository constraints, IGameweekConfigRepository config,
        IGameRosterRepository roster, ILogger<SetPlayAsUseCase> logger)
    {
        _teams = teams;
        _guard = guard;
        _squadView = squadView;
        _lineup = lineup;
        _constraints = constraints;
        _config = config;
        _roster = roster;
        _logger = logger;
    }

    public async Task<SetPlayAsResult> ExecuteAsync(
        string userId, string playerId, string? position, BuyPlayerContext context, CancellationToken ct)
    {
        if (!await _teams.ExistsAsync(userId, GameFlavor.Fantasy, ct))
            return SetPlayAsResult.NoTeam.Instance;

        var teamId = GameTeamId.For(userId, GameFlavor.Fantasy);
        // Before any validation read or write, so locked rounds freeze the old pins.
        await _guard.EnsureSnapshotsAsync(teamId, null, ct);

        if (await _squadView.ExecuteAsync(userId, context.Season, context.TournamentId, context.RuleSetVersion, ct)
            is not GetSquadResult.Found found)
            return SetPlayAsResult.RuleSetNotFound.Instance;

        var player = found.View.Players.FirstOrDefault(p => string.Equals(p.PlayerId, playerId, StringComparison.Ordinal));
        if (player is null) return SetPlayAsResult.NotInSquad.Instance;

        string? pin = null;
        if (position is not null)
        {
            pin = position.Trim().ToUpperInvariant();
            if (!player.NaturalPositions.Contains(pin, StringComparer.Ordinal))
                return SetPlayAsResult.NotEligible.Instance;

            var rejected = await ValidateSavedLineupAsync(teamId, found.View.Players, player, pin, ct);
            if (rejected is not null) return rejected;
        }

        var outcome = await _roster.SetPlayAsAsync(teamId, playerId, pin, ct);
        if (outcome == SetPlayAsOutcome.Conflict) return SetPlayAsResult.Conflict.Instance;
        if (outcome == SetPlayAsOutcome.NotFound) return SetPlayAsResult.NotInSquad.Instance;

        var view = await _squadView.ExecuteAsync(userId, context.Season, context.TournamentId, context.RuleSetVersion, ct);
        if (view is not GetSquadResult.Found updated) return SetPlayAsResult.RuleSetNotFound.Instance;

        _logger.LogInformation("Position pin changed for team {TeamId}, player {PlayerId}: {Position}",
            teamId, playerId, pin ?? "cleared");
        return new SetPlayAsResult.Updated(updated.View);
    }

    // Only a saved lineup can be broken by a pin; the default lineup is rebuilt around pins. Other
    // pre-existing violations (e.g. a missing captain) must not block the pin, so only position rules reject.
    private async Task<SetPlayAsResult?> ValidateSavedLineupAsync(
        string teamId, IReadOnlyList<SquadPlayer> squad, SquadPlayer player, string pin, CancellationToken ct)
    {
        var saved = await _lineup.GetAsync(teamId, ct);
        if (saved is null) return null;

        var config = await _config.GetAsync(DefaultVersion, ct);
        var constraints = await _constraints.GetAsync(config?.LineupConstraintsVersion ?? DefaultVersion, ct);
        if (constraints is null) return SetPlayAsResult.RuleSetNotFound.Instance;

        var withPin = squad.Select(p => ReferenceEquals(p, player) ? p with { PlayAs = pin } : p).ToList();
        var violations = LineupValidator.Validate(saved, withPin, constraints).Violations
            .Where(v => v.Code is "position_min" or "position_max")
            .ToList();
        return violations.Count > 0 ? new SetPlayAsResult.Rejected(violations) : null;
    }
}
