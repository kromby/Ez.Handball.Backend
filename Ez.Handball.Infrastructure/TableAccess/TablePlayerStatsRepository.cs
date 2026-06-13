using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TablePlayerStatsRepository : IPlayerStatsRepository
{
    private readonly ITableQuery _query;
    private readonly ILogger<TablePlayerStatsRepository> _logger;

    public TablePlayerStatsRepository(ITableQuery query, ILogger<TablePlayerStatsRepository> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlayerStat>> GetByPlayerAsync(string playerId, CancellationToken ct)
    {
        var stats = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(
                           Tables.PlayerStats, $"RowKey eq '{ODataFilter.Escape(playerId)}'", ct))
        {
            stats.Add(row);
        }
        if (stats.Count == 0) return Array.Empty<PlayerStat>();

        var nameByKey = new Dictionary<(string Season, string TournamentId), string>();
        var seasons = stats.Select(s => s.Season)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();

        foreach (var season in seasons)
        {
            await foreach (var t in _query.QueryAsync<TournamentEntity>(
                               Tables.Tournaments, $"PartitionKey eq '{season}'", ct))
            {
                nameByKey[(t.PartitionKey, t.RowKey)] = t.Name;
            }
        }

        var result = new List<PlayerStat>(stats.Count);
        foreach (var s in stats)
        {
            string? name = null;
            if (!nameByKey.TryGetValue((s.Season, s.TournamentId), out name))
            {
                _logger.LogWarning(
                    "Tournament {TournamentId} (season {Season}) not found while resolving name for match {MatchId}",
                    s.TournamentId, s.Season, s.PartitionKey);
            }

            result.Add(new PlayerStat(
                PlayerId: s.RowKey,
                MatchId: s.PartitionKey,
                TournamentId: s.TournamentId,
                TournamentName: name,
                Season: s.Season,
                TeamId: s.TeamId,
                ClubName: s.ClubName,
                Goals: s.Goals,
                YellowCards: s.YellowCards,
                TwoMinuteSuspensions: s.TwoMinuteSuspensions,
                RedCards: s.RedCards));
        }

        return result;
    }

    public async Task<IReadOnlyList<PlayerStat>> GetByMatchAsync(string matchId, CancellationToken ct)
    {
        var result = new List<PlayerStat>();
        await foreach (var s in _query.QueryAsync<PlayerStatEntity>(
                           Tables.PlayerStats, $"PartitionKey eq '{ODataFilter.Escape(matchId)}'", ct))
        {
            result.Add(new PlayerStat(
                PlayerId: s.RowKey,
                MatchId: s.PartitionKey,
                TournamentId: s.TournamentId,
                TournamentName: null,
                Season: s.Season,
                TeamId: s.TeamId,
                ClubName: s.ClubName,
                Goals: s.Goals,
                YellowCards: s.YellowCards,
                TwoMinuteSuspensions: s.TwoMinuteSuspensions,
                RedCards: s.RedCards));
        }
        return result;
    }
}
