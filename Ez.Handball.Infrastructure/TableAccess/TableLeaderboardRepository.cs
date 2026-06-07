using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableLeaderboardRepository : ILeaderboardRepository
{
    private readonly ITableQuery _query;
    private readonly ILogger<TableLeaderboardRepository> _logger;

    public TableLeaderboardRepository(ITableQuery query, ILogger<TableLeaderboardRepository> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LeaderboardEntry>> GetRankedAsync(LeaderboardQuery q, CancellationToken ct)
    {
        // A non-null but empty scope means "matched no tournaments" → no results
        // (distinct from null, which means "no tournament narrowing requested").
        if (q.TournamentIds is { Count: 0 })
            return Array.Empty<LeaderboardEntry>();

        var filter = BuildFilter(q);

        var rows = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(Tables.PlayerStats, filter, ct))
            rows.Add(row);

        if (!string.IsNullOrEmpty(q.Gender))
            rows = rows.Where(r => GenderOf(r.TeamId) == q.Gender).ToList();

        if (rows.Count == 0) return Array.Empty<LeaderboardEntry>();

        var aggregates = rows
            .GroupBy(r => r.RowKey)
            .Select(g => BuildAggregate(g.Key, g.ToList()))
            .OrderByDescending(a => MetricValue(a, q.Metric))
            .ThenBy(a => a.Games)
            .ThenBy(a => a.PlayerId, StringComparer.Ordinal)
            .ToList();

        var nameById = new Dictionary<string, string>();
        await foreach (var p in _query.QueryAsync<PlayerEntity>(Tables.Players, null, ct))
            nameById[p.RowKey] = p.Name;

        var result = new List<LeaderboardEntry>(aggregates.Count);
        for (var i = 0; i < aggregates.Count; i++)
        {
            var a = aggregates[i];
            if (!nameById.TryGetValue(a.PlayerId, out var name))
            {
                _logger.LogWarning(
                    "Player {PlayerId} not found in Players table while building leaderboard", a.PlayerId);
            }

            result.Add(new LeaderboardEntry(
                Rank: i + 1,
                PlayerId: a.PlayerId,
                Name: name,
                ClubId: a.ClubId,
                ClubName: a.ClubName,
                Gender: a.Gender,
                Games: a.Games,
                Goals: a.Goals,
                YellowCards: a.YellowCards,
                TwoMinuteSuspensions: a.TwoMinuteSuspensions,
                RedCards: a.RedCards,
                AvgGoals: Math.Round((double)a.Goals / a.Games, 2)));
        }

        return result;
    }

    private static string? BuildFilter(LeaderboardQuery q)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(q.Season))
            clauses.Add($"Season eq '{ODataFilter.Escape(q.Season)}'");

        if (q.TournamentIds is { Count: > 0 })
        {
            var ors = string.Join(" or ",
                q.TournamentIds.Select(id => $"TournamentId eq '{ODataFilter.Escape(id)}'"));
            // Parenthesize when ORing multiple ids, or when this clause must AND with another.
            var needsParens = q.TournamentIds.Count > 1 || clauses.Count > 0;
            clauses.Add(needsParens ? $"({ors})" : ors);
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    private static Aggregate BuildAggregate(string playerId, List<PlayerStatEntity> rows)
    {
        var games = rows.Count;
        var goals = rows.Sum(r => r.Goals);
        var yellow = rows.Sum(r => r.YellowCards);
        var twoMin = rows.Sum(r => r.TwoMinuteSuspensions);
        var red = rows.Sum(r => r.RedCards);

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

        return new Aggregate(
            playerId, club.ClubId, club.ClubName, GenderOf(club.TeamId),
            games, goals, yellow, twoMin, red);
    }

    private static int MetricValue(Aggregate a, LeaderboardMetric metric) => metric switch
    {
        LeaderboardMetric.Goals => a.Goals,
        LeaderboardMetric.YellowCards => a.YellowCards,
        LeaderboardMetric.TwoMinuteSuspensions => a.TwoMinuteSuspensions,
        LeaderboardMetric.RedCards => a.RedCards,
        LeaderboardMetric.Games => a.Games,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
    };

    private static string ClubIdOf(string teamId) => teamId.Split('-', 2)[0];

    private static string GenderOf(string teamId)
    {
        var parts = teamId.Split('-', 2);
        return parts.Length == 2 ? parts[1] : string.Empty;
    }

    private sealed record Aggregate(
        string PlayerId, string ClubId, string? ClubName, string Gender,
        int Games, int Goals, int YellowCards, int TwoMinuteSuspensions, int RedCards);
}
