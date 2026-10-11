using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed record SnapshotGuardResult(Gameweek? CurrentGameweek, bool CurrentGameweekLocked);

public interface IGameweekSnapshotGuard
{
    // Freezes any locked-but-unsnapshotted gameweek for this team, then reports the current editable
    // gameweek (earliest Open) and whether at least one gameweek is currently locked.
    Task<SnapshotGuardResult> EnsureSnapshotsAsync(string teamId, int? configVersion, CancellationToken ct);
}

public sealed class GameweekSnapshotGuard : IGameweekSnapshotGuard
{
    private const int DefaultVersion = 1;

    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly IGameweekLockRepository _locks;
    private readonly IGameweekLineupRepository _snapshots;
    private readonly IEffectiveLineupService _effective;
    private readonly IGetSquadUseCase _squad;
    private readonly ILineupConstraintsRepository _constraints;
    private readonly IGameTeamRepository _teams;
    private readonly TimeProvider _clock;

    public GameweekSnapshotGuard(
        IGameweekConfigRepository config, IGameweekCalendarService calendar, IGameweekLockRepository locks,
        IGameweekLineupRepository snapshots, IEffectiveLineupService effective, IGetSquadUseCase squad,
        ILineupConstraintsRepository constraints, IGameTeamRepository teams, TimeProvider clock)
    {
        _config = config;
        _calendar = calendar;
        _locks = locks;
        _snapshots = snapshots;
        _effective = effective;
        _squad = squad;
        _constraints = constraints;
        _teams = teams;
        _clock = clock;
    }

    public async Task<SnapshotGuardResult> EnsureSnapshotsAsync(string teamId, int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return new SnapshotGuardResult(null, false);

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return new SnapshotGuardResult(null, false);

        var now = _clock.GetUtcNow();
        var anyLocked = false;
        Lineup? live = null;
        var liveLoaded = false;
        DateTimeOffset? createdAt = null;
        var teamLoaded = false;

        foreach (var gw in calendar)
        {
            if (now < gw.Deadline) continue; // still Open → not locked
            anyLocked = true;

            // Pin the deadline the first time it's observed as passed (idempotent first-write-wins).
            await _locks.PinAsync(config.TournamentId, gw.RoundLabel, gw.Deadline, now, ct);

            // A team created at or after this deadline had no lineup for the round: freezing its
            // current (possibly half-built) squad here would wrongly score it (#158).
            if (!teamLoaded)
            {
                createdAt = await CreatedAtAsync(teamId, ct);
                teamLoaded = true;
            }
            if (createdAt >= gw.Deadline) continue;

            var existing = await _snapshots.GetSnapshotAsync(teamId, gw.RoundLabel, ct);
            if (existing is not null) continue;

            if (!liveLoaded)
            {
                live = await EffectiveLineupAsync(teamId, config, ct);
                liveLoaded = true;
            }
            if (live is not null)
                await _snapshots.SaveSnapshotAsync(teamId, gw.RoundLabel, live, ct);
        }

        var current = calendar.FirstOrDefault(g => g.Status == GameweekStatus.Open);
        return new SnapshotGuardResult(current, anyLocked);
    }

    private async Task<DateTimeOffset?> CreatedAtAsync(string teamId, CancellationToken ct)
    {
        var userId = GameTeamId.UserIdOf(teamId, GameFlavor.Fantasy);
        if (userId is null) return null;
        return (await _teams.GetAsync(userId, GameFlavor.Fantasy, ct))?.CreatedAt;
    }

    // The saved lineup, or — for a manager who never saved one — the default lineup from the squad
    // as it stands now, i.e. before the mutation this guard runs ahead of (#142), with pins stamped (#166).
    // Without the squad or constraints neither pins nor a default can be built, but a saved lineup is
    // still frozen as-is (scoring then uses natural positions), as it was before #166.
    private async Task<Lineup?> EffectiveLineupAsync(string teamId, GameweekConfig config, CancellationToken ct)
    {
        var userId = GameTeamId.UserIdOf(teamId, GameFlavor.Fantasy);
        if (userId is null) return await _effective.GetSavedAsync(teamId, ct);

        var constraints = await _constraints.GetAsync(config.LineupConstraintsVersion, ct);
        if (constraints is null) return await _effective.GetSavedAsync(teamId, ct);

        if (await _squad.ExecuteAsync(userId, null, null, null, ct) is not GetSquadResult.Found found)
            return await _effective.GetSavedAsync(teamId, ct);

        return await _effective.GetAsync(teamId, found.View.Players, constraints, ct);
    }
}
