using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public enum ClockMode { Set, AdvanceDeadline, AdvanceRound, Clear }

public abstract record AdvanceClockResult
{
    public sealed record Disabled : AdvanceClockResult { public static readonly Disabled Instance = new(); }
    public sealed record ConfigMissing : AdvanceClockResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record CalendarUnavailable : AdvanceClockResult { public static readonly CalendarUnavailable Instance = new(); }
    public sealed record NothingToAdvance : AdvanceClockResult { public static readonly NothingToAdvance Instance = new(); }
    public sealed record Cleared : AdvanceClockResult { public static readonly Cleared Instance = new(); }
    public sealed record Moved(DateTimeOffset VirtualNow, string? RoundLabel) : AdvanceClockResult;
}

public interface IAdvanceClockUseCase
{
    Task<AdvanceClockResult> ExecuteAsync(ClockMode mode, DateTimeOffset? date, int? configVersion, CancellationToken ct);
}

public sealed class AdvanceClockUseCase : IAdvanceClockUseCase
{
    private const int DefaultVersion = 1;

    private readonly IClockOverrideStore _store;
    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly TimeProvider _clock;
    private readonly bool _overrideEnabled;

    public AdvanceClockUseCase(
        IClockOverrideStore store, IGameweekConfigRepository config,
        IGameweekCalendarService calendar, TimeProvider clock, bool overrideEnabled)
    {
        _store = store;
        _config = config;
        _calendar = calendar;
        _clock = clock;
        _overrideEnabled = overrideEnabled;
    }

    public async Task<AdvanceClockResult> ExecuteAsync(
        ClockMode mode, DateTimeOffset? date, int? configVersion, CancellationToken ct)
    {
        // The override row is a no-op unless the master flag is on (it would be silently ignored
        // by GameClock). Refuse rather than appear to succeed.
        if (!_overrideEnabled) return AdvanceClockResult.Disabled.Instance;

        if (mode == ClockMode.Clear)
        {
            await _store.ClearAsync(ct);
            return AdvanceClockResult.Cleared.Instance;
        }

        if (mode == ClockMode.Set)
        {
            // The endpoint guarantees date is present for Set; guard for direct callers.
            if (date is null) return AdvanceClockResult.NothingToAdvance.Instance;
            var utc = date.Value.ToUniversalTime();
            await _store.SetAsync(utc, ct);
            return new AdvanceClockResult.Moved(utc, null);
        }

        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return AdvanceClockResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return AdvanceClockResult.CalendarUnavailable.Instance;

        var now = _clock.GetUtcNow();

        if (mode == ClockMode.AdvanceDeadline)
        {
            var next = calendar
                .Where(g => g.Deadline > now)
                .OrderBy(g => g.Deadline)
                .FirstOrDefault();
            if (next is null) return AdvanceClockResult.NothingToAdvance.Instance;
            await _store.SetAsync(next.Deadline, ct);
            return new AdvanceClockResult.Moved(next.Deadline, next.RoundLabel);
        }

        // AdvanceRound: advance to the next round boundary that lies in the future. Target =
        // a round's last fixture + the finality buffer, which exactly trips the `date + buffer <= now`
        // finality gate so the whole round reads ready. Only consider rounds whose target is strictly
        // after now and take the earliest such target: a past-but-never-final round (e.g. a postponed
        // fixture whose status never becomes "S") must not rewind the virtual clock.
        var buffer = TimeSpan.FromHours(config.MatchFinalBufferHours);
        // The Matches.Count > 0 guard is defensive: GameweekCalendarService builds every gameweek
        // from a non-empty round group, so a zero-match gameweek never occurs — the guard only keeps
        // the Max below from ever seeing an empty sequence.
        var nextRound = calendar
            .Where(g => g.Matches.Count > 0 && !g.Matches.All(m => m.IsFinal))
            .Select(g => new { Round = g, Target = g.Matches.Max(m => m.Date) + buffer })
            .Where(x => x.Target > now)
            .OrderBy(x => x.Target)
            .FirstOrDefault();
        if (nextRound is null) return AdvanceClockResult.NothingToAdvance.Instance;
        await _store.SetAsync(nextRound.Target, ct);
        return new AdvanceClockResult.Moved(nextRound.Target, nextRound.Round.RoundLabel);
    }
}
