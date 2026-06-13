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
    private readonly IGameweekScoringService _scoring;

    public SettleGameweekUseCase(
        IGameweekConfigRepository config, IGameweekCalendarService calendar,
        IGameweekLineupRepository snapshots, ILineupRepository liveLineup,
        IGameweekScoreRepository scores, IGetSquadUseCase squad, IPlayerStatsRepository stats,
        IScoringRuleSetRepository ruleSets, ILineupConstraintsRepository constraints,
        IGameweekScoringService scoring)
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
        _scoring = scoring;
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

        // Only settle once every member match is final (results complete). Postponed match → not yet.
        if (gw.Matches.Count == 0 || !gw.Matches.All(m => m.IsFinal))
            return SettleGameweekResult.NotReady.Instance;

        var ruleSet = await _ruleSets.GetAsync(GameFlavor.Fantasy, config.ScoringRuleSetVersion, ct);
        if (ruleSet is null) return SettleGameweekResult.RuleSetMissing.Instance;

        var constraints = await _constraints.GetAsync(config.LineupConstraintsVersion, ct);
        if (constraints is null) return SettleGameweekResult.RuleSetMissing.Instance;

        // Snapshot-if-missing: freeze the live lineup (unchanged since the deadline) before scoring.
        var snapshot = await _snapshots.GetSnapshotAsync(teamId, roundLabel, ct);
        if (snapshot is null)
        {
            var live = await _liveLineup.GetAsync(teamId, ct);
            if (live is null) return SettleGameweekResult.NoSnapshotPossible.Instance;
            await _snapshots.SaveSnapshotAsync(teamId, roundLabel, live, ct);
            snapshot = live;
        }

        var squadResult = await _squad.ExecuteAsync(userId, null, null, null, ct);
        if (squadResult is not GetSquadResult.Found found)
            return SettleGameweekResult.SquadNotFound.Instance;

        // Build playerId → aggregated stats across the gameweek's member matches (presence = played).
        var played = await BuildPlayedStatsAsync(gw, ct);

        var score = _scoring.Score(teamId, roundLabel, snapshot, found.View.Players, played, ruleSet, constraints);
        await _scores.SaveAsync(score, ct);
        return new SettleGameweekResult.Settled(score);
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
                        RedCards = cur.RedCards + s.RedCards
                    };
                else
                    acc[s.PlayerId] = new AggregatedStats(1, s.Goals, s.YellowCards, s.TwoMinuteSuspensions, s.RedCards);
            }
        }
        return acc;
    }
}
