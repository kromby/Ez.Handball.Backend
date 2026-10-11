using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

// One locked round of a league member's team: the lineup frozen at the deadline and, once the
// round is settled, its score. Either may be missing, never both.
public sealed record MemberGameweek(
    int Number, string RoundLabel, DateTimeOffset Deadline, Lineup? Lineup, GameweekScore? Score);

// Players maps every playerId in the lineups and breakdowns to its current Player row.
public sealed record MemberGameweeks(
    double RunningTotal, IReadOnlyList<MemberGameweek> Gameweeks, IReadOnlyDictionary<string, Player> Players);

public abstract record GetLeagueMemberGameweeksResult
{
    // Unknown league, caller not a member, or target not a member — deliberately indistinguishable
    // so the endpoint doesn't reveal whether a league or user exists.
    public sealed record NotFound : GetLeagueMemberGameweeksResult { public static readonly NotFound Instance = new(); }
    public sealed record ConfigMissing : GetLeagueMemberGameweeksResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record CalendarNotFound : GetLeagueMemberGameweeksResult { public static readonly CalendarNotFound Instance = new(); }
    public sealed record Found(MemberGameweeks View) : GetLeagueMemberGameweeksResult;
}

public interface IGetLeagueMemberGameweeksUseCase
{
    Task<GetLeagueMemberGameweeksResult> ExecuteAsync(
        string leagueId, string callerUserId, string targetUserId, int? configVersion, CancellationToken ct);
}

// A rival's team, limited to rounds whose deadline has passed (Backend#156). The live squad and
// lineup are never read here, so transfers and lineup changes stay hidden until the deadline.
public sealed class GetLeagueMemberGameweeksUseCase : IGetLeagueMemberGameweeksUseCase
{
    private const int DefaultVersion = 1;

    private readonly IMiniLeagueRepository _leagues;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly IGameweekSnapshotGuard _guard;
    private readonly IGameweekLineupRepository _snapshots;
    private readonly IGetMyGameweekScoresUseCase _scores;
    private readonly IPlayerRepository _players;
    private readonly TimeProvider _clock;

    public GetLeagueMemberGameweeksUseCase(
        IMiniLeagueRepository leagues, IGameweekConfigRepository config, IGameweekCalendarService calendar,
        IGameweekSnapshotGuard guard, IGameweekLineupRepository snapshots, IGetMyGameweekScoresUseCase scores,
        IPlayerRepository players, TimeProvider clock)
    {
        _leagues = leagues;
        _config = config;
        _calendar = calendar;
        _guard = guard;
        _snapshots = snapshots;
        _scores = scores;
        _players = players;
        _clock = clock;
    }

    public async Task<GetLeagueMemberGameweeksResult> ExecuteAsync(
        string leagueId, string callerUserId, string targetUserId, int? configVersion, CancellationToken ct)
    {
        if (!await BothAreMembersAsync(leagueId, callerUserId, targetUserId, ct))
            return GetLeagueMemberGameweeksResult.NotFound.Instance;

        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return GetLeagueMemberGameweeksResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return GetLeagueMemberGameweeksResult.CalendarNotFound.Instance;

        var now = _clock.GetUtcNow();
        var locked = calendar.Where(gw => now >= gw.Deadline).OrderBy(gw => gw.Number).ToList();
        var teamId = GameTeamId.For(targetUserId, GameFlavor.Fantasy);

        // A member who hasn't touched their team since a deadline has no snapshot for that round yet.
        // Their live lineup is unchanged since then (every mutation runs the guard first), so freezing
        // it now is exactly what a mutation or settlement would have frozen.
        await _guard.EnsureSnapshotsAsync(teamId, configVersion, ct);

        var scores = await _scores.ExecuteAsync(targetUserId, ct);
        var scoreByRound = scores.Gameweeks.ToDictionary(s => s.RoundLabel, StringComparer.Ordinal);

        var rounds = new List<MemberGameweek>();
        foreach (var gw in locked)
        {
            var lineup = await _snapshots.GetSnapshotAsync(teamId, gw.RoundLabel, ct);
            scoreByRound.TryGetValue(gw.RoundLabel, out var score);
            if (lineup is null && score is null) continue; // team didn't exist yet (#158)
            rounds.Add(new MemberGameweek(gw.Number, gw.RoundLabel, gw.Deadline, lineup, score));
        }

        var players = await ResolvePlayersAsync(rounds, scores.Players, ct);
        return new GetLeagueMemberGameweeksResult.Found(
            new MemberGameweeks(rounds.Sum(r => r.Score?.Points ?? 0), rounds, players));
    }

    private async Task<bool> BothAreMembersAsync(
        string leagueId, string callerUserId, string targetUserId, CancellationToken ct)
    {
        if (await _leagues.GetAsync(leagueId, ct) is null) return false;

        var memberIds = (await _leagues.GetMembersAsync(leagueId, ct))
            .Select(m => m.UserId)
            .ToHashSet(StringComparer.Ordinal);
        return memberIds.Contains(callerUserId) && memberIds.Contains(targetUserId);
    }

    // Only players referenced by a returned round are included, so nothing leaks from a round that
    // isn't locked. Breakdown players are already resolved by the scores use case; the rest (lineups
    // of unsettled rounds) are looked up here.
    private async Task<IReadOnlyDictionary<string, Player>> ResolvePlayersAsync(
        IReadOnlyList<MemberGameweek> rounds, IReadOnlyDictionary<string, Player> known, CancellationToken ct)
    {
        var referenced = rounds
            .SelectMany(r => (r.Lineup?.Slots.Select(s => s.PlayerId) ?? Enumerable.Empty<string>())
                .Concat(r.Score?.Breakdown.Select(b => b.PlayerId) ?? Enumerable.Empty<string>()))
            .Distinct(StringComparer.Ordinal);

        var players = new Dictionary<string, Player>(StringComparer.Ordinal);
        foreach (var id in referenced)
        {
            var player = known.TryGetValue(id, out var cached) ? cached : await _players.GetByIdAsync(id, ct);
            if (player is not null) players[id] = player;
        }
        return players;
    }
}
