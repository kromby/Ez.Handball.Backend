using System.Text.Json;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public class ParsePlayersFunction
{
    private readonly ITableWriter _tableWriter;

    public ParsePlayersFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("ParsePlayers")]
    public async Task RunAsync(
        [BlobTrigger("raw/matches/{matchId}/players-{clubId}.json", Connection = "HandballStorageConnection")] string blobContent,
        string matchId,
        string clubId,
        FunctionContext context)
    {
        var logger = context.GetLogger<ParsePlayersFunction>();
        await ProcessAsync(blobContent, matchId, clubId, logger);
    }

    public async Task ProcessAsync(string blobContent, string matchId, string clubId, ILogger? logger = null)
    {
        var response = JsonSerializer.Deserialize<PlayerStatsResponse>(blobContent);
        var players = response?.Data;

        if (players is null)
        {
            logger?.LogError("Failed to deserialize player stats for matchId {MatchId}, clubId {ClubId}", matchId, clubId);
            return;
        }

        var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'");

        if (matches.Count == 0)
        {
            // Throw so the blob trigger retries — ParseMatch may not have completed yet.
            throw new InvalidOperationException(
                $"Match {matchId} not found in Matches table; will retry");
        }

        var match = matches[0];
        var tournamentId = match.PartitionKey;
        var teamId = match.HomeTeamId.StartsWith($"{clubId}-")
            ? match.HomeTeamId
            : match.AwayTeamId;

        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>(
            "Tournaments", $"RowKey eq '{tournamentId}'");
        string season = string.Empty;
        if (tournaments is { Count: > 0 })
        {
            season = tournaments[0].PartitionKey;
        }
        else
        {
            logger?.LogWarning(
                "Tournament {TournamentId} not found in Tournaments table for match {MatchId}; PlayerStatEntity.Season will be empty",
                tournamentId, matchId);
        }

        // teamId is "{clubId}-{gender}" — split once and reuse for every player.
        var dashIndex = teamId.IndexOf('-');
        var derivedClubId = dashIndex > 0 ? teamId[..dashIndex] : string.Empty;
        var derivedGender = dashIndex > 0 ? teamId[(dashIndex + 1)..] : string.Empty;

        ClubEntity? club = null;
        if (!string.IsNullOrEmpty(derivedClubId))
        {
            club = await _tableWriter.GetAsync<ClubEntity>("Clubs", "club", derivedClubId);
            if (club is null)
            {
                logger?.LogWarning(
                    "Club {ClubId} not found in Clubs table for match {MatchId}; PlayerEntity.ClubName will be null",
                    derivedClubId, matchId);
            }
        }

        foreach (var player in players.Where(p => p.Player == "1"))
        {
            var playerId = player.PlayerId;

            _ = int.TryParse(player.Goals, out var goals);
            _ = int.TryParse(player.YellowCards, out var yellowCards);
            _ = int.TryParse(player.TwoMinuteSuspensions, out var twoMinuteSuspensions);
            _ = int.TryParse(player.RedCards, out var redCards);

            await _tableWriter.UpsertAsync("Players", new PlayerEntity
            {
                PartitionKey = teamId,
                RowKey = playerId,
                Name = player.Name,
                Position = player.Position,
                JerseyNumber = player.PlayerJerseyNumber,
                DateOfBirth = ParseDateOfBirth(player.Identifier),
                Gender = derivedGender,
                ClubId = derivedClubId,
                ClubName = club?.Name
            });

            await _tableWriter.UpsertAsync("PlayerStats", new PlayerStatEntity
            {
                PartitionKey = matchId,
                RowKey = playerId,
                Goals = goals,
                YellowCards = yellowCards,
                TwoMinuteSuspensions = twoMinuteSuspensions,
                RedCards = redCards,
                TournamentId = tournamentId,
                Season = season
            });
        }

        logger?.LogInformation(
            "Parsed player stats for match {MatchId}, club {ClubId}, team {TeamId}",
            matchId, clubId, teamId);
    }

    private static DateTimeOffset? ParseDateOfBirth(string? identifier)
    {
        if (identifier is null || identifier.Length < 6) return null;
        var century = identifier[^1] switch { '9' => 1900, '0' => 2000, _ => -1 };
        if (century < 0) return null;
        var ddmmyy = identifier[..6];
        if (!int.TryParse(ddmmyy[4..6], out var yy)) return null;
        var ddmmyyyy = $"{ddmmyy[..4]}{century + yy:D4}";
        return DateTimeOffset.TryParseExact(ddmmyyyy, "ddMMyyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dob) ? dob : null;
    }
}
