using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TablePlayerPoolRepository : IPlayerPoolRepository
{
    private readonly ITableQuery _query;
    private readonly ILogger<TablePlayerPoolRepository> _logger;

    public TablePlayerPoolRepository(ITableQuery query, ILogger<TablePlayerPoolRepository> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PooledPlayer>> GetAggregatedAsync(PlayerPoolQuery q, CancellationToken ct)
    {
        // Non-null but empty scope = "matched no tournaments" => no results.
        if (q.TournamentIds is { Count: 0 })
            return Array.Empty<PooledPlayer>();

        var filter = BuildFilter(q);

        var rows = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(Tables.PlayerStats, filter, ct))
            rows.Add(row);

        if (!string.IsNullOrEmpty(q.Gender))
            rows = rows.Where(r => GenderOf(r.TeamId) == q.Gender).ToList();

        if (rows.Count == 0) return Array.Empty<PooledPlayer>();

        // Join the Players table once for name + position.
        var playerById = new Dictionary<string, PlayerEntity>();
        await foreach (var p in _query.QueryAsync<PlayerEntity>(Tables.Players, null, ct))
            playerById[p.RowKey] = p;

        var previousStatsByPlayer = await GetPreviousSeasonStatsByPlayerAsync(q, ct);

        var result = rows
            .GroupBy(r => r.RowKey)
            .Select(g =>
            {
                var (clubId, clubName, gender) = ResolveClub(g.ToList());
                playerById.TryGetValue(g.Key, out var player);
                if (player is null)
                    _logger.LogWarning(
                        "Player {PlayerId} not found in Players table while building pool", g.Key);

                var saves = g.Sum(r => r.HbStatzSaves ?? 0);
                var shotsFaced = g.Sum(r => r.HbStatzShotsFaced ?? 0);
                var stats = new AggregatedStats(
                    Games: g.Count(),
                    Goals: g.Sum(r => r.Goals),
                    YellowCards: g.Sum(r => r.YellowCards),
                    TwoMinuteSuspensions: g.Sum(r => r.TwoMinuteSuspensions),
                    RedCards: g.Sum(r => r.RedCards),
                    Assists: g.Sum(r => r.HbStatzAssists ?? 0),
                    Steals: g.Sum(r => r.HbStatzSteals ?? 0),
                    Blocks: g.Sum(r => r.HbStatzBlocks ?? 0),
                    Saves: saves,
                    Turnovers: g.Sum(r => r.HbStatzTurnovers ?? 0),
                    LegalStops: g.Sum(r => r.HbStatzLegalStops ?? 0),
                    Shots: g.Sum(r => r.HbStatzShots ?? 0),
                    ExpectedGoals: g.Sum(r => r.HbStatzExpectedGoals ?? 0),
                    ShotsFaced: shotsFaced,
                    SavePct: AggregatedStats.ComputeSavePct(saves, shotsFaced),
                    ExpectedSaves: g.Sum(r => r.HbStatzExpectedSaves ?? 0),
                    GradeTotal: AggregatedStats.AverageGrade(g.Select(r => r.HbStatzGradeTotal)),
                    GradeOffense: AggregatedStats.AverageGrade(g.Select(r => r.HbStatzGradeOffense)),
                    GradeDefense: AggregatedStats.AverageGrade(g.Select(r => r.HbStatzGradeDefense)),
                    GradeGoalkeeping: AggregatedStats.AverageGrade(g.Select(r => r.HbStatzGradeGoalkeeping)));

                previousStatsByPlayer.TryGetValue(g.Key, out var previousStats);

                return new PooledPlayer(
                    PlayerId: g.Key,
                    Name: player?.Name,
                    ClubId: clubId,
                    ClubName: clubName,
                    Gender: gender,
                    Position: player?.Position ?? string.Empty,
                    Stats: stats,
                    Retired: player?.Retired == true,
                    PreviousSeasonStats: previousStats,
                    PositionSecondary: player?.PositionSecondary);
            })
            .ToList();

        return result;
    }

    // Fetches and aggregates PlayerStats for the already-resolved previous-season
    // scope. Not gender-filtered: results are only ever looked up by PlayerId
    // against the current-season roster above, so an unused entry for the other
    // gender is simply never read.
    private async Task<Dictionary<string, AggregatedStats>> GetPreviousSeasonStatsByPlayerAsync(
        PlayerPoolQuery q, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(q.PreviousSeason)) return new Dictionary<string, AggregatedStats>();
        if (q.PreviousSeasonTournamentIds is { Count: 0 }) return new Dictionary<string, AggregatedStats>();

        var previousFilter = BuildFilter(new PlayerPoolQuery(q.PreviousSeason, q.PreviousSeasonTournamentIds, null));

        var previousRows = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(Tables.PlayerStats, previousFilter, ct))
            previousRows.Add(row);

        return previousRows
            .GroupBy(r => r.RowKey)
            .ToDictionary(g => g.Key, g => new AggregatedStats(
                Games: g.Count(),
                Goals: g.Sum(r => r.Goals),
                YellowCards: g.Sum(r => r.YellowCards),
                TwoMinuteSuspensions: g.Sum(r => r.TwoMinuteSuspensions),
                RedCards: g.Sum(r => r.RedCards)));
    }

    private static string? BuildFilter(PlayerPoolQuery q)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(q.Season))
            clauses.Add($"Season eq '{ODataFilter.Escape(q.Season)}'");

        if (q.TournamentIds is { Count: > 0 })
        {
            var ors = string.Join(" or ",
                q.TournamentIds.Select(id => $"TournamentId eq '{ODataFilter.Escape(id)}'"));
            var needsParens = q.TournamentIds.Count > 1 || clauses.Count > 0;
            clauses.Add(needsParens ? $"({ors})" : ors);
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    // The club the player scored most for in scope (matches leaderboard tie-breaks).
    private static (string ClubId, string? ClubName, string Gender) ResolveClub(List<PlayerStatEntity> rows)
    {
        var club = rows
            .GroupBy(r => ClubIdOf(r.TeamId))
            .Select(cg => new
            {
                ClubId = cg.Key,
                Goals = cg.Sum(r => r.Goals),
                Games = cg.Count(),
                ClubName = cg.Select(r => r.ClubName).FirstOrDefault(n => n != null),
                TeamId = cg.First().TeamId
            })
            .OrderByDescending(c => c.Goals)
            .ThenByDescending(c => c.Games)
            .ThenBy(c => c.ClubId, StringComparer.Ordinal)
            .First();

        return (club.ClubId, club.ClubName, GenderOf(club.TeamId));
    }

    private static string ClubIdOf(string teamId) => teamId.Split('-', 2)[0];

    private static string GenderOf(string teamId)
    {
        var parts = teamId.Split('-', 2);
        return parts.Length == 2 ? parts[1] : string.Empty;
    }
}
