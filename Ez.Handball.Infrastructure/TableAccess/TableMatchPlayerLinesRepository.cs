using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableMatchPlayerLinesRepository : IMatchPlayerLinesRepository
{
    private readonly ITableQuery _query;
    private readonly ILogger<TableMatchPlayerLinesRepository> _logger;

    public TableMatchPlayerLinesRepository(ITableQuery query, ILogger<TableMatchPlayerLinesRepository> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<MatchPlayerLine>>> GetByMatchAsync(
        string matchId, CancellationToken ct)
    {
        // PlayerStats is partitioned by matchId, so one query returns both teams' lines.
        var stats = new List<PlayerStatEntity>();
        await foreach (var s in _query.QueryAsync<PlayerStatEntity>(
                           Tables.PlayerStats, $"PartitionKey eq '{ODataFilter.Escape(matchId)}'", ct))
        {
            stats.Add(s);
        }
        if (stats.Count == 0)
            return new Dictionary<string, IReadOnlyList<MatchPlayerLine>>();

        // Roster lookup (name/jersey/position) per distinct team that has stat rows.
        var rosters = new Dictionary<string, PlayerEntity>();
        foreach (var teamId in stats.Select(s => s.TeamId).Where(t => !string.IsNullOrEmpty(t)).Distinct())
        {
            await foreach (var p in _query.QueryAsync<PlayerEntity>(
                               Tables.Players, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
            {
                rosters[$"{teamId}|{p.RowKey}"] = p;
            }
        }

        var result = new Dictionary<string, IReadOnlyList<MatchPlayerLine>>();
        foreach (var group in stats.GroupBy(s => s.TeamId))
        {
            result[group.Key] = group
                .Select(s => ToLine(s, group.Key, rosters, matchId))
                .OrderBy(p => int.TryParse(p.JerseyNumber, out var n) ? n : int.MaxValue)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        return result;
    }

    private MatchPlayerLine ToLine(
        PlayerStatEntity stat, string teamId,
        IReadOnlyDictionary<string, PlayerEntity> rosters, string matchId)
    {
        rosters.TryGetValue($"{teamId}|{stat.RowKey}", out var roster);
        if (roster is null)
        {
            _logger.LogWarning(
                "Player {PlayerId} has a stat row but no Players entry for team {TeamId} in match {MatchId}",
                stat.RowKey, teamId, matchId);
        }

        return new MatchPlayerLine(
            PlayerId: stat.RowKey,
            Name: roster?.Name,
            JerseyNumber: roster?.JerseyNumber,
            Position: roster?.Position,
            Goals: stat.Goals,
            YellowCards: stat.YellowCards,
            TwoMinuteSuspensions: stat.TwoMinuteSuspensions,
            RedCards: stat.RedCards);
    }
}
