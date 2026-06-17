using Ez.Handball.Application.Abstractions;
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
    private readonly ILineupRepository _liveLineup;
    private readonly TimeProvider _clock;

    public GameweekSnapshotGuard(
        IGameweekConfigRepository config, IGameweekCalendarService calendar, IGameweekLockRepository locks,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup, TimeProvider clock)
    {
        _config = config;
        _calendar = calendar;
        _locks = locks;
        _snapshots = snapshots;
        _liveLineup = liveLineup;
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

        foreach (var gw in calendar)
        {
            if (now < gw.Deadline) continue; // still Open → not locked
            anyLocked = true;

            // Pin the deadline the first time it's observed as passed (idempotent first-write-wins).
            await _locks.PinAsync(config.TournamentId, gw.RoundLabel, gw.Deadline, now, ct);

            var existing = await _snapshots.GetSnapshotAsync(teamId, gw.RoundLabel, ct);
            if (existing is not null) continue;

            if (!liveLoaded)
            {
                live = await _liveLineup.GetAsync(teamId, ct);
                liveLoaded = true;
            }
            if (live is not null)
                await _snapshots.SaveSnapshotAsync(teamId, gw.RoundLabel, live, ct);
        }

        var current = calendar.FirstOrDefault(g => g.Status == GameweekStatus.Open);
        return new SnapshotGuardResult(current, anyLocked);
    }
}
