using System.Globalization;
using System.Text.Json;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Parsing;

public class MatchParser : IMatchParser
{
    // Azure Table Storage's minimum supported Edm.DateTime is 1601-01-01 UTC.
    private static readonly DateTimeOffset TableStorageMinDate =
        new(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ITableWriter _tableWriter;
    private readonly ILogger<MatchParser> _logger;

    public MatchParser(ITableWriter tableWriter, ILogger<MatchParser> logger)
    {
        _tableWriter = tableWriter;
        _logger = logger;
    }

    public async Task ParseAsync(string blobContent, string matchId, CancellationToken ct = default)
    {
        var response = JsonSerializer.Deserialize<MatchDetailsResponse>(blobContent);
        var details = response?.Data;

        if (details is null)
        {
            _logger.LogError("Failed to deserialize match details for matchId {MatchId}", matchId);
            return;
        }

        var tournamentId = details.TournamentId;

        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>(
            "Tournaments", $"RowKey eq '{tournamentId}'", ct);

        if (tournaments.Count == 0)
        {
            _logger.LogError(
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

        // Azure Table Storage rejects dates before 1601-01-01 (Edm.DateTime min),
        // so the fallback for an unparseable date must stay within range — never
        // DateTimeOffset.MinValue (0001-01-01), which would throw on upsert.
        DateTimeOffset date = TableStorageMinDate;
        if (DateTimeOffset.TryParseExact(
                details.Date,
                "dd.MM.yyyy - HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedDate))
        {
            date = parsedDate;
        }
        else
        {
            _logger.LogWarning(
                "Could not parse match date '{Date}' for match {MatchId}; storing fallback date",
                details.Date, matchId);
        }

        await _tableWriter.UpsertAsync("Clubs", new ClubEntity
        {
            PartitionKey = "club",
            RowKey = homeClubId,
            Name = homeClubName
        }, ct);

        await _tableWriter.UpsertAsync("Clubs", new ClubEntity
        {
            PartitionKey = "club",
            RowKey = awayClubId,
            Name = awayClubName
        }, ct);

        await _tableWriter.UpsertAsync("Teams", new TeamEntity
        {
            PartitionKey = "team",
            RowKey = homeTeamId,
            ClubId = homeClubId,
            Gender = gender,
            Name = homeClubName
        }, ct);

        await _tableWriter.UpsertAsync("Teams", new TeamEntity
        {
            PartitionKey = "team",
            RowKey = awayTeamId,
            ClubId = awayClubId,
            Gender = gender,
            Name = awayClubName
        }, ct);

        _ = int.TryParse(details.HomeHalftimeGoals, out var homeHalftime);
        _ = int.TryParse(details.GuestHalftimeGoals, out var awayHalftime);
        int? attendance = int.TryParse(details.GameSpectators, out var spectators) ? spectators : null;

        await _tableWriter.UpsertAsync("Matches", new MatchEntity
        {
            PartitionKey = tournamentId,
            RowKey = matchId,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            HomeScore = homeScore,
            AwayScore = awayScore,
            Date = date,
            Status = details.ReportStatus,
            Venue = details.PlayingFieldName,
            Attendance = attendance,
            HomeHalftimeScore = homeHalftime,
            AwayHalftimeScore = awayHalftime
        }, ct);

        _logger.LogInformation(
            "Parsed match {MatchId} (tournament {TournamentId}, gender {Gender})",
            matchId, tournamentId, gender);
    }
}
