using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableMatchPlayerLinesRepository : IMatchPlayerLinesRepository
{
    // Keeps each OR'd RowKey filter well under Table Storage's 15-comparison limit.
    private const int FallbackChunkSize = 15;

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

        // A player whose Players row sits under another team's partition (e.g. not yet moved
        // after a transfer) still has a name — look them up by playerId across all partitions.
        var fallback = new Dictionary<string, PlayerEntity>();
        var missingIds = stats
            .Where(s => !rosters.ContainsKey($"{s.TeamId}|{s.RowKey}"))
            .Select(s => s.RowKey)
            .Distinct()
            .ToList();
        foreach (var chunk in missingIds.Chunk(FallbackChunkSize))
        {
            var filter = string.Join(" or ", chunk.Select(id => $"RowKey eq '{ODataFilter.Escape(id)}'"));
            await foreach (var p in _query.QueryAsync<PlayerEntity>(Tables.Players, filter, ct))
            {
                fallback.TryAdd(p.RowKey, p);
            }
        }

        var result = new Dictionary<string, IReadOnlyList<MatchPlayerLine>>();
        foreach (var group in stats.GroupBy(s => s.TeamId))
        {
            result[group.Key] = group
                .Select(s => ToLine(s, group.Key, rosters, fallback, matchId))
                .OrderBy(p => int.TryParse(p.JerseyNumber, out var n) ? n : int.MaxValue)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        return result;
    }

    private MatchPlayerLine ToLine(
        PlayerStatEntity stat, string teamId,
        IReadOnlyDictionary<string, PlayerEntity> rosters,
        IReadOnlyDictionary<string, PlayerEntity> fallback, string matchId)
    {
        rosters.TryGetValue($"{teamId}|{stat.RowKey}", out var roster);
        PlayerEntity? other = null;
        if (roster is null)
        {
            fallback.TryGetValue(stat.RowKey, out other);
            _logger.LogWarning(
                "Player {PlayerId} has a stat row but no Players entry for team {TeamId} in match {MatchId} (found under {OtherTeamId})",
                stat.RowKey, teamId, matchId, other?.PartitionKey ?? "no team");
        }

        return new MatchPlayerLine(
            PlayerId: stat.RowKey,
            Name: roster?.Name ?? other?.Name,
            // A jersey number belongs to a club, so another team's row can't supply it.
            JerseyNumber: roster?.JerseyNumber,
            Position: roster?.Position ?? other?.Position,
            Goals: stat.Goals,
            YellowCards: stat.YellowCards,
            TwoMinuteSuspensions: stat.TwoMinuteSuspensions,
            RedCards: stat.RedCards);
    }
}
