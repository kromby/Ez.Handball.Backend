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
    private readonly TimeProvider _clock;

    public GameweekCalendarService(
        IMatchRepository matches, IGameweekLockRepository locks, TimeProvider clock)
    {
        _matches = matches;
        _locks = locks;
        _clock = clock;
    }

    public async Task<IReadOnlyList<Gameweek>?> GetCalendarAsync(GameweekConfig config, CancellationToken ct)
    {
        var data = await _matches.ListByTournamentAsync(config.TournamentId, ct);
        if (data is null) return null;

        var now = _clock.GetUtcNow();
        var offset = TimeSpan.FromHours(config.LockOffsetHours);
        var finalBuffer = TimeSpan.FromHours(config.MatchFinalBufferHours);

        var ordered = data.Matches
            .GroupBy(m => m.Round)
            .OrderBy(g => RoundOrder.Key(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var result = new List<Gameweek>(ordered.Count);
        var number = 1;
        foreach (var group in ordered)
        {
            var roundLabel = group.Key;
            var members = group
                .Select(m => new GameweekMatch(
                    m.MatchId, m.Date, IsFinal(m.Status, m.Date, now, finalBuffer),
                    m.Home.TeamId, m.Away.TeamId))
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

    // Effective finality (#95): stored as final AND virtual now has passed the fixture by the buffer
    // (match duration + reporting slack). In production now is the wall clock, so a played fixture
    // trivially passes and real future matches are not "S" — the gate is a no-op there.
    private static bool IsFinal(string status, DateTimeOffset date, DateTimeOffset now, TimeSpan buffer)
        => status == "S" && date + buffer <= now;

    private static GameweekStatus ComputeStatus(
        DateTimeOffset now, DateTimeOffset deadline, IReadOnlyList<GameweekMatch> members)
    {
        if (now < deadline) return GameweekStatus.Open;
        var finalCount = members.Count(m => m.IsFinal);
        if (finalCount == 0) return GameweekStatus.DeadlineLocked;
        if (finalCount < members.Count) return GameweekStatus.InPlay;
        return GameweekStatus.Settled;
    }
}
