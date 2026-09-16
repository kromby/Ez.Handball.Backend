using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Parsing;

public class PlayerParser : IPlayerParser
{
    private readonly ITableWriter _tableWriter;
    private readonly ILogger<PlayerParser> _logger;

    public PlayerParser(ITableWriter tableWriter, ILogger<PlayerParser> logger)
    {
        _tableWriter = tableWriter;
        _logger = logger;
    }

    public async Task ParseAsync(string blobContent, string matchId, string clubId, CancellationToken ct = default)
    {
        var response = JsonSerializer.Deserialize<PlayerStatsResponse>(blobContent);
        var players = response?.Data;

        if (players is null)
        {
            _logger.LogError("Failed to deserialize player stats for matchId {MatchId}, clubId {ClubId}", matchId, clubId);
            return;
        }

        var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{matchId}'", ct);

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Match {matchId} not found in Matches table; will retry");
        }

        var match = matches[0];
        var tournamentId = match.PartitionKey;
        var teamId = match.HomeTeamId.StartsWith($"{clubId}-")
            ? match.HomeTeamId
            : match.AwayTeamId;

        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>(
            "Tournaments", $"RowKey eq '{tournamentId}'", ct);
        string season = string.Empty;
        if (tournaments is { Count: > 0 })
        {
            season = tournaments[0].PartitionKey;
        }
        else
        {
            _logger.LogWarning(
                "Tournament {TournamentId} not found in Tournaments table for match {MatchId}; PlayerStatEntity.Season will be empty",
                tournamentId, matchId);
        }

        var dashIndex = teamId.IndexOf('-');
        var derivedClubId = dashIndex > 0 ? teamId[..dashIndex] : string.Empty;
        var derivedGender = dashIndex > 0 ? teamId[(dashIndex + 1)..] : string.Empty;

        ClubEntity? club = null;
        if (!string.IsNullOrEmpty(derivedClubId))
        {
            club = await _tableWriter.GetAsync<ClubEntity>("Clubs", "club", derivedClubId, ct);
            if (club is null)
            {
                _logger.LogWarning(
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

            // Look up every existing row for this playerId, not just the one under teamId:
            // Table Storage can't rename a PartitionKey in place, so a player who has changed
            // clubs since we last saw them still has their old row sitting under the old
            // club's partition. Querying across all partitions lets a transfer both (a) inherit
            // a previously-known position instead of falling back to hsi.is's unreliable
            // POSITION field, and (b) get cleaned up below instead of lingering forever.
            var existingRows = await _tableWriter.QueryAsync<PlayerEntity>(
                "Players", $"RowKey eq '{Escape(playerId)}'", ct);
            var existingPlayer = existingRows.FirstOrDefault(p => p.PartitionKey == teamId)
                ?? existingRows.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Position));
            var position = existingPlayer is not null && !string.IsNullOrWhiteSpace(existingPlayer.Position)
                ? existingPlayer.Position
                : player.Position;

            await _tableWriter.UpsertAsync("Players", new PlayerEntity
            {
                PartitionKey = teamId,
                RowKey = playerId,
                Name = player.Name,
                Position = position,
                JerseyNumber = player.PlayerJerseyNumber,
                DateOfBirth = ParseDateOfBirth(player.Identifier),
                Gender = derivedGender,
                ClubId = derivedClubId,
                ClubName = club?.Name
                // Retired intentionally not set — Merge preserves the maintainer's value.
            }, ct, TableUpdateMode.Merge);

            // hsi.is says this player is on teamId's roster now, so any row left behind under a
            // different partition is a stale pre-transfer duplicate — delete it so every reader
            // that resolves a player by RowKey (the public player pool, admin tooling, HBStatz
            // aggregation) sees exactly one row.
            foreach (var stale in existingRows.Where(p => p.PartitionKey != teamId))
            {
                await _tableWriter.DeleteAsync("Players", stale.PartitionKey, stale.RowKey, ct);
            }

            await _tableWriter.UpsertAsync("PlayerStats", new PlayerStatEntity
            {
                PartitionKey = matchId,
                RowKey = playerId,
                Goals = goals,
                YellowCards = yellowCards,
                TwoMinuteSuspensions = twoMinuteSuspensions,
                RedCards = redCards,
                TournamentId = tournamentId,
                Season = season,
                TeamId = teamId,
                ClubName = club?.Name
            }, ct);
        }

        _logger.LogInformation(
            "Parsed player stats for match {MatchId}, club {ClubId}, team {TeamId}",
            matchId, clubId, teamId);
    }

    private static string Escape(string value) => value.Replace("'", "''");

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
