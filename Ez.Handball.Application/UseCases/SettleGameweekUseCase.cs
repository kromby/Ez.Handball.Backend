using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SettleGameweekResult
{
    public sealed record ConfigMissing : SettleGameweekResult { public static readonly ConfigMissing Instance = new(); }
    public sealed record NotFound : SettleGameweekResult { public static readonly NotFound Instance = new(); }   // unknown round/tournament
    public sealed record RuleSetMissing : SettleGameweekResult { public static readonly RuleSetMissing Instance = new(); }
    public sealed record NoSnapshotPossible : SettleGameweekResult { public static readonly NoSnapshotPossible Instance = new(); } // no live lineup to freeze
    public sealed record SquadNotFound : SettleGameweekResult { public static readonly SquadNotFound Instance = new(); } // owned squad couldn't be resolved
    public sealed record NotReady : SettleGameweekResult { public static readonly NotReady Instance = new(); }  // not all member matches final
    public sealed record TeamCreatedAfterDeadline : SettleGameweekResult { public static readonly TeamCreatedAfterDeadline Instance = new(); } // team didn't exist at the round's deadline
    public sealed record Settled(GameweekScore Score) : SettleGameweekResult;
}

public interface ISettleGameweekUseCase
{
    // userId is needed to resolve the owned squad (for positions); teamId is the GameTeamId.
    Task<SettleGameweekResult> ExecuteAsync(
        string userId, string teamId, string roundLabel, int? configVersion, CancellationToken ct);
}

public sealed class SettleGameweekUseCase : ISettleGameweekUseCase
{
    private const int DefaultVersion = 1;

    private readonly IGameweekConfigRepository _config;
    private readonly IGameweekCalendarService _calendar;
    private readonly IGameweekLineupRepository _snapshots;
    private readonly ILineupRepository _liveLineup;
    private readonly IGameweekScoreRepository _scores;
    private readonly IGetSquadUseCase _squad;
    private readonly IPlayerStatsRepository _stats;
    private readonly IScoringRuleSetRepository _ruleSets;
    private readonly ILineupConstraintsRepository _constraints;
    private readonly IGameTeamRepository _teams;
    private readonly IGameweekScoringService _scoring;
    private readonly ITransferConfigRepository _transferConfig;
    private readonly ITransferCountRepository _transferCounts;

    public SettleGameweekUseCase(
        IGameweekConfigRepository config, IGameweekCalendarService calendar,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup,
        IGameweekScoreRepository scores, IGetSquadUseCase squad, IPlayerStatsRepository stats,
        IScoringRuleSetRepository ruleSets, ILineupConstraintsRepository constraints,
        IGameTeamRepository teams, IGameweekScoringService scoring,
        ITransferConfigRepository transferConfig, ITransferCountRepository transferCounts)
    {
        _config = config;
        _calendar = calendar;
        _snapshots = snapshots;
        _liveLineup = liveLineup;
        _scores = scores;
        _squad = squad;
        _stats = stats;
        _ruleSets = ruleSets;
        _constraints = constraints;
        _teams = teams;
        _scoring = scoring;
        _transferConfig = transferConfig;
        _transferCounts = transferCounts;
    }

    // Caller contract: userId and teamId MUST refer to the same team (teamId == GameTeamId.For(userId, Fantasy)). The owned squad (for positions) is resolved from userId.
    public async Task<SettleGameweekResult> ExecuteAsync(
        string userId, string teamId, string roundLabel, int? configVersion, CancellationToken ct)
    {
        // Enforce the caller contract: the owned squad is resolved from userId, so a teamId that
        // doesn't belong to userId would score the wrong team's positions. Reject the mismatch.
        if (teamId != GameTeamId.For(userId, GameFlavor.Fantasy))
            return SettleGameweekResult.NotFound.Instance;

        var config = await _config.GetAsync(configVersion ?? DefaultVersion, ct);
        if (config is null) return SettleGameweekResult.ConfigMissing.Instance;

        var calendar = await _calendar.GetCalendarAsync(config, ct);
        if (calendar is null) return SettleGameweekResult.NotFound.Instance;

        var gw = calendar.FirstOrDefault(g => g.RoundLabel == roundLabel);
        if (gw is null) return SettleGameweekResult.NotFound.Instance;

        // A team created at or after the deadline had no lineup for this round, so it isn't scored (#158).
        // A score already stored for the round is left as it is.
        var team = await _teams.GetAsync(userId, GameFlavor.Fantasy, ct);
        if (team is not null && team.CreatedAt >= gw.Deadline)
            return SettleGameweekResult.TeamCreatedAfterDeadline.Instance;

        // Only settle once every member match is final (results complete). Postponed match → not yet.
        if (gw.Matches.Count == 0 || !gw.Matches.All(m => m.IsFinal))
            return SettleGameweekResult.NotReady.Instance;

        var ruleSet = await _ruleSets.GetAsync(GameFlavor.Fantasy, config.ScoringRuleSetVersion, ct);
        if (ruleSet is null) return SettleGameweekResult.RuleSetMissing.Instance;

        var constraints = await _constraints.GetAsync(config.LineupConstraintsVersion, ct);
        if (constraints is null) return SettleGameweekResult.RuleSetMissing.Instance;

        var squadResult = await _squad.ExecuteAsync(userId, null, null, null, ct);
        if (squadResult is not GetSquadResult.Found found)
            return SettleGameweekResult.SquadNotFound.Instance;

        // Snapshot-if-missing: freeze the live lineup (unchanged since the deadline) before scoring.
        // A manager who never saved a lineup plays their default one (#142).
        var snapshot = await _snapshots.GetSnapshotAsync(teamId, roundLabel, ct);
        if (snapshot is null)
        {
            var live = await _liveLineup.GetAsync(teamId, ct);
            var effective = live ?? DefaultLineup.From(found.View.Players, constraints);
            if (effective.Slots.Count == 0) return SettleGameweekResult.NoSnapshotPossible.Instance;
            await _snapshots.SaveSnapshotAsync(teamId, roundLabel, effective, ct);
            snapshot = effective;
        }

        // Build playerId → aggregated stats across the gameweek's member matches (presence = played).
        var played = await BuildPlayedStatsAsync(gw, ct);

        var gross = _scoring.Score(teamId, roundLabel, snapshot, found.View.Players, played, ruleSet, constraints);
        var hit = await TransferHitAsync(teamId, team?.CreatedAt, calendar, roundLabel, ct);
        var score = gross with { Points = gross.Points - hit, TransferHit = hit };
        await _scores.SaveAsync(score, ct);
        return new SettleGameweekResult.Settled(score);
    }

    // Points deducted for transfers beyond the round's free allowance (#171). Reuses the calendar
    // already loaded for this settle rather than going through IGetTransferAllowanceUseCase, which
    // would re-read it for every team on every auto-settle tick. No transfer config = rules off = 0.
    // Stable on re-settle: once a round's deadline passes, new transfers count toward a later round.
    private async Task<int> TransferHitAsync(
        string teamId, DateTimeOffset? teamCreatedAt, IReadOnlyList<Gameweek> calendar, string roundLabel,
        CancellationToken ct)
    {
        var config = await _transferConfig.GetAsync(DefaultVersion, ct);
        if (config is null) return 0;

        var counts = await _transferCounts.ListAsync(teamId, ct);
        // Wildcard rounds come from chip state once it exists (#173).
        var allowance = TransferAllowanceCalculator.Compute(
            config, calendar, counts, teamCreatedAt, roundLabel, wildcardRounds: null);
        return allowance?.PointsHit ?? 0;
    }

    private async Task<IReadOnlyDictionary<string, AggregatedStats>> BuildPlayedStatsAsync(
        Gameweek gw, CancellationToken ct)
    {
        var acc = new Dictionary<string, AggregatedStats>(StringComparer.Ordinal);
        foreach (var match in gw.Matches)
        {
            foreach (var s in await _stats.GetByMatchAsync(match.MatchId, ct))
            {
                if (acc.TryGetValue(s.PlayerId, out var cur))
                    acc[s.PlayerId] = cur with
                    {
                        Games = cur.Games + 1,
                        Goals = cur.Goals + s.Goals,
                        YellowCards = cur.YellowCards + s.YellowCards,
                        TwoMinuteSuspensions = cur.TwoMinuteSuspensions + s.TwoMinuteSuspensions,
                        RedCards = cur.RedCards + s.RedCards,
                        Assists = cur.Assists + (s.HbStatzAssists ?? 0),
                        Steals = cur.Steals + (s.HbStatzSteals ?? 0),
                        Blocks = cur.Blocks + (s.HbStatzBlocks ?? 0),
                        Saves = cur.Saves + (s.HbStatzSaves ?? 0)
                    };
                else
                    acc[s.PlayerId] = new AggregatedStats(
                        1, s.Goals, s.YellowCards, s.TwoMinuteSuspensions, s.RedCards,
                        s.HbStatzAssists ?? 0, s.HbStatzSteals ?? 0, s.HbStatzBlocks ?? 0, s.HbStatzSaves ?? 0);
            }
        }
        return acc;
    }
}
