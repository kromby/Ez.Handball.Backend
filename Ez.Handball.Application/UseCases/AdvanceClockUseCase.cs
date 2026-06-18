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

        // AdvanceRound: the first round (calendar order) not yet all-final at the current clock.
        // Target = its last fixture + the finality buffer, which exactly trips the
        // `date + buffer <= now` finality gate so the whole round reads ready.
        var buffer = TimeSpan.FromHours(config.MatchFinalBufferHours);
        var round = calendar.FirstOrDefault(g => g.Matches.Count > 0 && !g.Matches.All(m => m.IsFinal));
        if (round is null) return AdvanceClockResult.NothingToAdvance.Instance;
        var target = round.Matches.Max(m => m.Date) + buffer;
        await _store.SetAsync(target, ct);
        return new AdvanceClockResult.Moved(target, round.RoundLabel);
    }
}
