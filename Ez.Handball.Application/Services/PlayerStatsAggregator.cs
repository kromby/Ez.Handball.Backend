using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed class PlayerStatsAggregator : IPlayerStatsAggregator
{
    private readonly IPlayerStatsRepository _stats;
    private readonly ITournamentScopeResolver _scope;

    // Memoizes the last-fetched player's rows so that AggregateAsync and
    // AggregatePreviousSeasonAsync, called back-to-back for the same player
    // (as PlayerPriceService does), don't each independently trigger a full
    // cross-partition table scan.
    private string? _cachedPlayerId;
    private IReadOnlyList<PlayerStat>? _cachedRows;

    public PlayerStatsAggregator(IPlayerStatsRepository stats, ITournamentScopeResolver scope)
    {
        _stats = stats;
        _scope = scope;
    }

    private async Task<IReadOnlyList<PlayerStat>> GetPlayerRowsAsync(string playerId, CancellationToken ct)
    {
        if (_cachedPlayerId == playerId && _cachedRows is not null) return _cachedRows;
        var rows = await _stats.GetByPlayerAsync(playerId, ct);
        _cachedPlayerId = playerId;
        _cachedRows = rows;
        return rows;
    }

    public async Task<AggregatedStats> AggregateAsync(
        string playerId, string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct)
    {
        var resolved = await _scope.ResolveSeasonLabelAsync(season, ct);
        if (resolved is null) return new AggregatedStats(0, 0, 0, 0, 0);

        var ids = await _scope.ResolveTournamentIdsAsync(resolved, tournamentId, competitionId, type, ct);

        var rows = await GetPlayerRowsAsync(playerId, ct);
        var scoped = rows.Where(r => r.Season == resolved);
        if (ids is not null)
            scoped = scoped.Where(r => ids.Contains(r.TournamentId));

        var list = scoped.ToList();
        return BuildAggregate(list);
    }

    public async Task<AggregatedStats?> AggregatePreviousSeasonAsync(
        string playerId, string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct)
    {
        var previous = await _scope.ResolvePreviousSeasonScopeAsync(season, tournamentId, competitionId, type, ct);
        if (previous is null) return null;
        if (previous.TournamentIds is { Count: 0 }) return null;

        var rows = await GetPlayerRowsAsync(playerId, ct);
        var scoped = rows.Where(r => r.Season == previous.SeasonLabel);
        if (previous.TournamentIds is not null)
            scoped = scoped.Where(r => previous.TournamentIds.Contains(r.TournamentId));

        var list = scoped.ToList();
        if (list.Count == 0) return null;

        return BuildAggregate(list);
    }

    private static AggregatedStats BuildAggregate(IReadOnlyList<PlayerStat> list)
    {
        var saves = list.Sum(r => r.HbStatzSaves ?? 0);
        var shotsFaced = list.Sum(r => r.HbStatzShotsFaced ?? 0);
        return new AggregatedStats(
            Games: list.Count,
            Goals: list.Sum(r => r.Goals),
            YellowCards: list.Sum(r => r.YellowCards),
            TwoMinuteSuspensions: list.Sum(r => r.TwoMinuteSuspensions),
            RedCards: list.Sum(r => r.RedCards),
            Assists: list.Sum(r => r.HbStatzAssists ?? 0),
            Steals: list.Sum(r => r.HbStatzSteals ?? 0),
            Blocks: list.Sum(r => r.HbStatzBlocks ?? 0),
            Saves: saves,
            Turnovers: list.Sum(r => r.HbStatzTurnovers ?? 0),
            LegalStops: list.Sum(r => r.HbStatzLegalStops ?? 0),
            Shots: list.Sum(r => r.HbStatzShots ?? 0),
            ExpectedGoals: list.Sum(r => r.HbStatzExpectedGoals ?? 0),
            ShotsFaced: shotsFaced,
            SavePct: AggregatedStats.ComputeSavePct(saves, shotsFaced),
            ExpectedSaves: list.Sum(r => r.HbStatzExpectedSaves ?? 0),
            GradeTotal: AggregatedStats.AverageGrade(list.Select(r => r.HbStatzGradeTotal)),
            GradeOffense: AggregatedStats.AverageGrade(list.Select(r => r.HbStatzGradeOffense)),
            GradeDefense: AggregatedStats.AverageGrade(list.Select(r => r.HbStatzGradeDefense)),
            GradeGoalkeeping: AggregatedStats.AverageGrade(list.Select(r => r.HbStatzGradeGoalkeeping)));
    }
}
