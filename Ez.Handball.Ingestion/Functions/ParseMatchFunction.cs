using System.Globalization;
using System.Text.Json;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public class ParseMatchFunction
{
    private readonly ITableWriter _tableWriter;

    public ParseMatchFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("ParseMatch")]
    public async Task RunAsync(
        [BlobTrigger("raw/matches/{matchId}/details.json", Connection = "HandballStorageConnection")] string blobContent,
        string matchId,
        FunctionContext context)
    {
        var logger = context.GetLogger<ParseMatchFunction>();
        await ProcessAsync(blobContent, matchId, logger);
    }

    public async Task ProcessAsync(string blobContent, string matchId, ILogger? logger = null)
    {
        var response = JsonSerializer.Deserialize<MatchDetailsResponse>(blobContent);
        var details = response?.Data;

        if (details is null)
        {
            logger?.LogError("Failed to deserialize match details for matchId {MatchId}", matchId);
            return;
        }

        var tournamentId = details.TournamentId;

        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>(
            "Tournaments", $"RowKey eq '{tournamentId}'");

        if (tournaments.Count == 0)
        {
            logger?.LogError(
                "Tournament {TournamentId} not found in table — skipping match {MatchId}",
                tournamentId, matchId);
            return;
        }

        var tournament = tournaments[0];
        var gender = tournament.Gender;

        var homeClubId = details.ClubHomeId;
        var homeClubName = details.HomeClubName;
        var awayClubId = details.ClubGuestId;
        var awayClubName = details.GuestClubName;

        var homeTeamId = $"{homeClubId}-{gender}";
        var awayTeamId = $"{awayClubId}-{gender}";

        _ = int.TryParse(details.GamesResultHome, out var homeScore);
        _ = int.TryParse(details.GamesResultGuest, out var awayScore);

        DateTimeOffset date = DateTimeOffset.MinValue;
        if (DateTimeOffset.TryParseExact(
                details.Date,
                "dd.MM.yyyy - HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedDate))
        {
            date = parsedDate;
        }

        // Upsert home club
        await _tableWriter.UpsertAsync("Clubs", new ClubEntity
        {
            PartitionKey = "club",
            RowKey = homeClubId,
            Name = homeClubName
        });

        // Upsert away club
        await _tableWriter.UpsertAsync("Clubs", new ClubEntity
        {
            PartitionKey = "club",
            RowKey = awayClubId,
            Name = awayClubName
        });

        // Upsert home team
        await _tableWriter.UpsertAsync("Teams", new TeamEntity
        {
            PartitionKey = "team",
            RowKey = homeTeamId,
            ClubId = homeClubId,
            Gender = gender,
            Name = homeClubName
        });

        // Upsert away team
        await _tableWriter.UpsertAsync("Teams", new TeamEntity
        {
            PartitionKey = "team",
            RowKey = awayTeamId,
            ClubId = awayClubId,
            Gender = gender,
            Name = awayClubName
        });

        // Upsert match
        await _tableWriter.UpsertAsync("Matches", new MatchEntity
        {
            PartitionKey = tournamentId,
            RowKey = matchId,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            HomeScore = homeScore,
            AwayScore = awayScore,
            Date = date,
            Status = details.ReportStatus
        });

        logger?.LogInformation(
            "Parsed match {MatchId} (tournament {TournamentId}, gender {Gender})",
            matchId, tournamentId, gender);
    }
}
