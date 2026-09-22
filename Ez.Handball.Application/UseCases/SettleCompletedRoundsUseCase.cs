using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SettleCompletedRoundsResult
{
    public sealed record ConfigMissing : SettleCompletedRoundsResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record CalendarUnavailable : SettleCompletedRoundsResult { public static readonly CalendarUnavailable Instance = new(); }
    // A team-independent failure for one round (config/rule set) — the remaining rounds are not attempted.
    public sealed record RoundFailed(string Round, SettleRoundForAllTeamsResult Reason) : SettleCompletedRoundsResult;
    public sealed record Completed(IReadOnlyList<SettleRoundReport> Rounds) : SettleCompletedRoundsResult;
}

public interface ISettleCompletedRoundsUseCase
{
    // Settles every team for each complete gameweek (all member matches final). With a recentWindow,
    // only rounds whose last match started within that window of now are (re)settled; null settles all.
    Task<SettleCompletedRoundsResult> ExecuteAsync(TimeSpan? recentWindow, int? configVersion, CancellationToken ct);
}

// Production settlement driver (#136). Settlement is replace-mode and idempotent, so re-settling a
// recent round is safe and deliberately picks up stats (e.g. HBStatz enrichment) that arrive late.
public sealed class SettleCompletedRoundsUseCase : ISettleCompletedRoundsUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly ISettleRoundForAllTeamsUseCase _settleRound;
    private readonly TimeProvider _clock;

    public SettleCompletedRoundsUseCase(
        IGameweekConfigRepository config, IGameweekCalendarService calendar,
        ISettleRoundForAllTeamsUseCase settleRound, TimeProvider clock)
    {
        _config = config;
        _calendar = calendar;
        _settleRound = settleRound;
        _clock = clock;
    }

    public async Task<SettleCompletedRoundsResult> ExecuteAsync(
        TimeSpan? recentWindow, int? configVersion, CancellationToken ct)
    {
        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return SettleCompletedRoundsResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return SettleCompletedRoundsResult.CalendarUnavailable.Instance;

        var cutoff = recentWindow is null ? (DateTimeOffset?)null : _clock.GetUtcNow() - recentWindow.Value;
        var rounds = calendar
            .Where(gw => gw.Status == GameweekStatus.Settled && gw.Matches.Count > 0)
            .Where(gw => cutoff is null || gw.Matches.Max(m => m.Date) >= cutoff)
            .Select(gw => gw.RoundLabel)
            .ToList();

        var reports = new List<SettleRoundReport>(rounds.Count);
        foreach (var round in rounds)
        {
            var result = await _settleRound.ExecuteAsync(round, configVersion, ct);
            if (result is not SettleRoundForAllTeamsResult.Completed completed)
                return new SettleCompletedRoundsResult.RoundFailed(round, result);
            reports.Add(completed.Report);
        }

        return new SettleCompletedRoundsResult.Completed(reports);
    }
}
