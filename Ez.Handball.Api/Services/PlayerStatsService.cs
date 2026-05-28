using Ez.Handball.Api.Models;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Api.Services;

public class PlayerStatsService : IPlayerStatsService
{
    private readonly ITableQuery _query;
    private readonly ILogger<PlayerStatsService> _logger;

    public PlayerStatsService(ITableQuery query, ILogger<PlayerStatsService> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlayerStatRow>> GetStatsAsync(
        string playerId, CancellationToken ct = default)
    {
        var stats = new List<PlayerStatEntity>();
        await foreach (var row in _query.QueryAsync<PlayerStatEntity>(
                           Tables.PlayerStats, $"RowKey eq '{playerId}'", ct))
        {
            stats.Add(row);
        }

        if (stats.Count == 0) return Array.Empty<PlayerStatRow>();

        // Resolve tournament names with one query per distinct season.
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

        var rows = new List<PlayerStatRow>(stats.Count);
        foreach (var s in stats)
        {
            string? name = null;
            if (!nameByKey.TryGetValue((s.Season, s.TournamentId), out name))
            {
                _logger.LogWarning(
                    "Tournament {TournamentId} (season {Season}) not found while resolving name for match {MatchId}",
                    s.TournamentId, s.Season, s.PartitionKey);
            }

            rows.Add(new PlayerStatRow(
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

        return rows;
    }
}
