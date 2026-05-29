using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.TableAccess;

internal sealed class TableMatchRepository : IMatchRepository
{
    private readonly ITableQuery _query;
    private readonly ILogger<TableMatchRepository> _logger;

    public TableMatchRepository(ITableQuery query, ILogger<TableMatchRepository> logger)
    {
        _query = query;
        _logger = logger;
    }

    public async Task<MatchDetail?> GetByIdAsync(string matchId, CancellationToken ct)
    {
        var escapedMatchId = ODataFilter.Escape(matchId);

        MatchEntity? match = null;
        await foreach (var row in _query.QueryAsync<MatchEntity>(
                           Tables.Matches, $"RowKey eq '{escapedMatchId}'", ct))
        {
            match = row;
            break;
        }
        if (match is null) return null;

        string? tournamentName = null;
        string season = string.Empty;
        await foreach (var t in _query.QueryAsync<TournamentEntity>(
                           Tables.Tournaments, $"RowKey eq '{ODataFilter.Escape(match.PartitionKey)}'", ct))
        {
            tournamentName = t.Name;
            season = t.PartitionKey;
            break;
        }
        if (tournamentName is null)
        {
            _logger.LogWarning(
                "Tournament {TournamentId} not found while building match {MatchId}",
                match.PartitionKey, matchId);
        }

        var homeTeamId = match.HomeTeamId;
        var awayTeamId = match.AwayTeamId;
        var teamFilter =
            $"PartitionKey eq 'team' and (RowKey eq '{ODataFilter.Escape(homeTeamId)}' or RowKey eq '{ODataFilter.Escape(awayTeamId)}')";
        var teamNames = new Dictionary<string, string>();
        await foreach (var team in _query.QueryAsync<TeamEntity>(Tables.Teams, teamFilter, ct))
        {
            teamNames[team.RowKey] = team.Name;
        }

        var stats = new List<PlayerStatEntity>();
        await foreach (var s in _query.QueryAsync<PlayerStatEntity>(
                           Tables.PlayerStats, $"PartitionKey eq '{escapedMatchId}'", ct))
        {
            stats.Add(s);
        }

        var rosters = new Dictionary<string, PlayerEntity>();
        foreach (var teamId in new[] { homeTeamId, awayTeamId })
        {
            await foreach (var p in _query.QueryAsync<PlayerEntity>(
                               Tables.Players, $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", ct))
            {
                rosters[$"{teamId}|{p.RowKey}"] = p;
            }
        }

        var homeTeam = BuildTeam(homeTeamId, match.HomeScore, match.HomeHalftimeScore, teamNames, stats, rosters, matchId);
        var awayTeam = BuildTeam(awayTeamId, match.AwayScore, match.AwayHalftimeScore, teamNames, stats, rosters, matchId);

        return new MatchDetail(
            MatchId: matchId,
            TournamentId: match.PartitionKey,
            TournamentName: tournamentName,
            Season: season,
            Date: match.Date,
            Venue: string.IsNullOrEmpty(match.Venue) ? null : match.Venue,
            Attendance: match.Attendance,
            Status: match.Status,
            HomeTeam: homeTeam,
            AwayTeam: awayTeam);
    }

    private MatchTeam BuildTeam(
        string teamId, int finalScore, int halftimeScore,
        IReadOnlyDictionary<string, string> teamNames,
        IReadOnlyList<PlayerStatEntity> allStats,
        IReadOnlyDictionary<string, PlayerEntity> rosters,
        string matchId)
    {
        var clubId = teamId.Split('-', 2)[0];

        var teamStats = allStats.Where(s => s.TeamId == teamId).ToList();

        string? clubName;
        if (teamNames.TryGetValue(teamId, out var name))
            clubName = name;
        else
            clubName = teamStats.Select(s => s.ClubName).FirstOrDefault(n => !string.IsNullOrEmpty(n));

        var players = teamStats
            .Select(s =>
            {
                rosters.TryGetValue($"{teamId}|{s.RowKey}", out var roster);
                if (roster is null)
                {
                    _logger.LogWarning(
                        "Player {PlayerId} has a stat row but no Players entry for team {TeamId} in match {MatchId}",
                        s.RowKey, teamId, matchId);
                }
                return new MatchPlayerLine(
                    PlayerId: s.RowKey,
                    Name: roster?.Name,
                    JerseyNumber: roster?.JerseyNumber,
                    Position: roster?.Position,
                    Goals: s.Goals,
                    YellowCards: s.YellowCards,
                    TwoMinuteSuspensions: s.TwoMinuteSuspensions,
                    RedCards: s.RedCards);
            })
            .OrderBy(p => int.TryParse(p.JerseyNumber, out var n) ? n : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Floor at 0: corrupt source data where halftime > final must not leak a negative half.
        var score = new LineScore(halftimeScore, Math.Max(0, finalScore - halftimeScore), finalScore);

        return new MatchTeam(teamId, clubId, clubName, score, players);
    }
}
