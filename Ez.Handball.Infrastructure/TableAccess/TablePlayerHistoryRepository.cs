using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TablePlayerHistoryRepository : IPlayerHistoryRepository
{
    private readonly ITableQuery _query;
    private readonly ILogger<TablePlayerHistoryRepository> _logger;

    public TablePlayerHistoryRepository(ITableQuery query, ILogger<TablePlayerHistoryRepository> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<PlayerHistory> GetByPlayerAsync(string playerId, CancellationToken ct)
    {
        var stats = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(
                           Tables.PlayerStats, $"RowKey eq '{ODataFilter.Escape(playerId)}'", ct))
        {
            stats.Add(row);
        }
        if (stats.Count == 0) return new PlayerHistory(Array.Empty<PlayerHistoryEntry>(), null);

        var meta = new Dictionary<(string Season, string TournamentId), (string Name, int Priority)>();
        foreach (var season in stats.Select(s => s.Season).Where(s => !string.IsNullOrEmpty(s)).Distinct())
        {
            await foreach (var t in _query.QueryAsync<TournamentEntity>(
                               Tables.Tournaments, $"PartitionKey eq '{season}'", ct))
            {
                meta[(t.PartitionKey, t.RowKey)] = (t.Name, t.Priority);
            }
        }

        var entries = stats
            .GroupBy(s => (s.Season, s.TournamentId, s.TeamId))
            .Select(g =>
            {
                var first = g.First();
                var games = g.Count();
                var goals = g.Sum(s => s.Goals);
                var yellow = g.Sum(s => s.YellowCards);
                var twoMin = g.Sum(s => s.TwoMinuteSuspensions);
                var red = g.Sum(s => s.RedCards);

                string? name = null;
                int priority = int.MaxValue;
                if (meta.TryGetValue((first.Season, first.TournamentId), out var m))
                {
                    name = m.Name;
                    priority = m.Priority;
                }
                else
                {
                    _logger.LogWarning(
                        "Tournament {TournamentId} (season {Season}) not found while building history for player {PlayerId}",
                        first.TournamentId, first.Season, playerId);
                }

                var clubId = first.TeamId.Split('-', 2)[0];

                return (Entry: new PlayerHistoryEntry(
                    Season: first.Season,
                    TournamentId: first.TournamentId,
                    TournamentName: name,
                    ClubId: clubId,
                    ClubName: first.ClubName,
                    Games: games,
                    TotalGoals: goals,
                    TotalYellowCards: yellow,
                    TotalTwoMinuteSuspensions: twoMin,
                    TotalRedCards: red,
                    AvgGoals: (double)goals / games,
                    AvgYellowCards: (double)yellow / games,
                    AvgTwoMinuteSuspensions: (double)twoMin / games,
                    AvgRedCards: (double)red / games),
                    Priority: priority);
            })
            .OrderByDescending(x => x.Entry.Season, StringComparer.Ordinal)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Entry.ClubName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Entry)
            .ToList();

        var tGames  = entries.Sum(e => e.Games);
        var tGoals  = entries.Sum(e => e.TotalGoals);
        var tYellow = entries.Sum(e => e.TotalYellowCards);
        var tTwoMin = entries.Sum(e => e.TotalTwoMinuteSuspensions);
        var tRed    = entries.Sum(e => e.TotalRedCards);

        var totals = new PlayerHistoryTotals(
            tGames,
            tGoals, tYellow, tTwoMin, tRed,
            (double)tGoals  / tGames,
            (double)tYellow / tGames,
            (double)tTwoMin / tGames,
            (double)tRed    / tGames);

        return new PlayerHistory(entries, totals);
    }
}
