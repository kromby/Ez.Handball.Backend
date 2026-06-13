using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public interface IGameweekCalendarService
{
    // The derived gameweek calendar for the configured fantasy tournament, ordered by number.
    // Null when the tournament id is unknown (mirrors IMatchRepository.ListByTournamentAsync).
    Task<IReadOnlyList<Gameweek>?> GetCalendarAsync(GameweekConfig config, CancellationToken ct);
}

public sealed class GameweekCalendarService : IGameweekCalendarService
{
    private readonly IMatchRepository _matches;
    private readonly IGameweekLockRepository _locks;
    private readonly Func<DateTimeOffset> _now;

    public GameweekCalendarService(
        IMatchRepository matches, IGameweekLockRepository locks, Func<DateTimeOffset> now)
    {
        _matches = matches;
        _locks = locks;
        _now = now;
    }

    public async Task<IReadOnlyList<Gameweek>?> GetCalendarAsync(GameweekConfig config, CancellationToken ct)
    {
        var data = await _matches.ListByTournamentAsync(config.TournamentId, ct);
        if (data is null) return null;

        var now = _now();
        var offset = TimeSpan.FromHours(config.LockOffsetHours);

        var ordered = data.Matches
            .GroupBy(m => m.Round)
            .OrderBy(g => RoundSortKey(g.Key).Bucket)
            .ThenBy(g => RoundSortKey(g.Key).Value)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var result = new List<Gameweek>(ordered.Count);
        var number = 1;
        foreach (var group in ordered)
        {
            var roundLabel = group.Key;
            var members = group
                .Select(m => new GameweekMatch(m.MatchId, m.Date, IsFinal(m.Status), m.Home.TeamId, m.Away.TeamId))
                .OrderBy(m => m.Date)
                .ToList();

            // A GroupBy group is never empty, but guard defensively so Min can't throw if the
            // grouping source ever changes.
            if (members.Count == 0) continue;

            var derived = members.Min(m => m.Date) - offset;
            var pinned = await _locks.GetPinnedDeadlineAsync(config.TournamentId, roundLabel, ct);
            var deadline = pinned ?? derived;

            result.Add(new Gameweek(
                number++, roundLabel, config.TournamentId, deadline,
                ComputeStatus(now, deadline, members), members));
        }

        return result;
    }

    private static bool IsFinal(string status) => status == "S";

    private static GameweekStatus ComputeStatus(
        DateTimeOffset now, DateTimeOffset deadline, IReadOnlyList<GameweekMatch> members)
    {
        if (now < deadline) return GameweekStatus.Open;
        var finalCount = members.Count(m => m.IsFinal);
        if (finalCount == 0) return GameweekStatus.DeadlineLocked;
        if (finalCount < members.Count) return GameweekStatus.InPlay;
        return GameweekStatus.Settled;
    }

    // Numeric rounds first (ascending), text rounds (playoffs/finals) last — mirrors GetRoundsUseCase.
    private static (int Bucket, int Value) RoundSortKey(string round)
        => int.TryParse(round, out var n) ? (0, n) : (1, 0);
}
