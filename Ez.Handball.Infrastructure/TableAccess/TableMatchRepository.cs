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

    public async Task<MatchInfo?> GetByIdAsync(string matchId, CancellationToken ct)
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

        var teamFilter =
            $"PartitionKey eq 'team' and (RowKey eq '{ODataFilter.Escape(match.HomeTeamId)}' or RowKey eq '{ODataFilter.Escape(match.AwayTeamId)}')";
        var teamNames = new Dictionary<string, string>();
        await foreach (var team in _query.QueryAsync<TeamEntity>(Tables.Teams, teamFilter, ct))
        {
            teamNames[team.RowKey] = team.Name;
        }

        return new MatchInfo(
            MatchId: matchId,
            TournamentId: match.PartitionKey,
            TournamentName: tournamentName,
            Season: season,
            Date: match.Date,
            Venue: string.IsNullOrEmpty(match.Venue) ? null : match.Venue,
            Attendance: match.Attendance,
            Status: match.Status,
            HomeTeam: BuildTeam(match.HomeTeamId, match.HomeScore, match.HomeHalftimeScore, teamNames),
            AwayTeam: BuildTeam(match.AwayTeamId, match.AwayScore, match.AwayHalftimeScore, teamNames));
    }

    private static MatchTeamInfo BuildTeam(
        string teamId, int finalScore, int halftimeScore,
        IReadOnlyDictionary<string, string> teamNames)
    {
        var clubId = teamId.Split('-', 2)[0];
        var clubName = teamNames.TryGetValue(teamId, out var name) ? name : null;

        // Floor at 0: corrupt source data where halftime > final must not leak a negative half.
        var score = new LineScore(halftimeScore, Math.Max(0, finalScore - halftimeScore), finalScore);

        return new MatchTeamInfo(teamId, clubId, clubName, score);
    }
}
