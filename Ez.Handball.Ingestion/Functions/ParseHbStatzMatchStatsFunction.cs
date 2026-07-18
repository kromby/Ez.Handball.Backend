using System.Linq;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public class ParseHbStatzMatchStatsFunction
{
    private readonly ITableWriter _tableWriter;
    private readonly ILogger<ParseHbStatzMatchStatsFunction> _logger;

    public ParseHbStatzMatchStatsFunction(ITableWriter tableWriter, ILogger<ParseHbStatzMatchStatsFunction> logger)
    {
        _tableWriter = tableWriter;
        _logger = logger;
    }

    [Function("ParseHbStatzMatchStats")]
    public async Task RunAsync(
        [BlobTrigger("raw/hbstatz/matches/{matchId}/players-{side}.html", Connection = "HandballStorageConnection")] string htmlContent,
        string matchId,
        string side,
        FunctionContext context)
    {
        _logger.LogInformation("Parsing HBStatz stats for match {MatchId} ({Side} side)", matchId, side);

        var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{ODataFilter.Escape(matchId)}'", context.CancellationToken);
        if (matches.Count == 0)
        {
            _logger.LogError("Match {MatchId} not found in database; cannot reconcile HBStatz players.", matchId);
            return;
        }
        var match = matches[0];
        var teamId = side == "home" ? match.HomeTeamId : match.AwayTeamId;

        var tournamentId = match.PartitionKey;
        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>("Tournaments", $"RowKey eq '{ODataFilter.Escape(tournamentId)}'", context.CancellationToken);
        var season = tournaments.FirstOrDefault()?.PartitionKey ?? string.Empty;

        var roster = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"PartitionKey eq '{ODataFilter.Escape(teamId)}'", context.CancellationToken);
        var tables = StatsTableParser.ParseAll(htmlContent);
        var lines = MatchStatLineBuilder.Build(tables, side);

        var rosterList = roster as IReadOnlyList<PlayerEntity> ?? roster.ToList();

        foreach (var line in lines)
        {
            var playerId = PlayerReconciler.Reconcile(line, rosterList);
            if (playerId is null)
            {
                _logger.LogWarning("Could not reconcile HBStatz player '{Name}' (Jersey {Jersey}) in team {TeamId}", line.Name, line.Jersey, teamId);
                continue;
            }

            var existing = await _tableWriter.GetAsync<PlayerStatEntity>("PlayerStats", matchId, playerId, context.CancellationToken);
            if (existing is not null)
            {
                existing.Assists = line.Assists;
                existing.Turnovers = line.Turnovers;
                existing.Steals = line.Steals;
                existing.PlusMinus = line.PlusMinus;
                existing.Shots = line.Shots;
                existing.Blocks = line.Blocks;
                existing.Stops = line.Stops;
                existing.ExpectedGoals = line.ExpectedGoals;
                existing.PenaltiesEarned = line.PenaltiesEarned;
                existing.PenaltyGoals = line.PenaltyGoals;
                existing.Saves = line.Saves;
                existing.SaveRate = line.SaveRate;
                existing.ExpectedSaves = line.ExpectedSaves;
                existing.GoalsAgainst = line.GoalsAgainst;
                existing.PenaltySaves = line.PenaltySaves;

                await _tableWriter.UpsertAsync("PlayerStats", existing, context.CancellationToken, TableUpdateMode.Replace);
            }
            else
            {
                var newEntity = new PlayerStatEntity
                {
                    PartitionKey = matchId,
                    RowKey = playerId,
                    TournamentId = tournamentId,
                    Season = season,
                    TeamId = teamId,
                    Goals = line.Goals,
                    YellowCards = line.YellowCards,
                    TwoMinuteSuspensions = line.TwoMinuteSuspensions,
                    RedCards = line.RedCards,
                    Assists = line.Assists,
                    Turnovers = line.Turnovers,
                    Steals = line.Steals,
                    PlusMinus = line.PlusMinus,
                    Shots = line.Shots,
                    Blocks = line.Blocks,
                    Stops = line.Stops,
                    ExpectedGoals = line.ExpectedGoals,
                    PenaltiesEarned = line.PenaltiesEarned,
                    PenaltyGoals = line.PenaltyGoals,
                    Saves = line.Saves,
                    SaveRate = line.SaveRate,
                    ExpectedSaves = line.ExpectedSaves,
                    GoalsAgainst = line.GoalsAgainst,
                    PenaltySaves = line.PenaltySaves
                };

                await _tableWriter.UpsertAsync("PlayerStats", newEntity, context.CancellationToken, TableUpdateMode.Replace);
            }
        }

        _logger.LogInformation("Finished merging HBStatz stats for match {MatchId} ({Side} side)", matchId, side);
    }
}
