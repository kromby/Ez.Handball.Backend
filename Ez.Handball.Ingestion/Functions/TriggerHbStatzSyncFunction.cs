using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Parsing;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public record HbStatzSyncResult(
    int MatchesChecked, int MatchesSynced, IReadOnlyList<string> Unmatched, IReadOnlyList<string> Failed);

// Admin-triggered enrichment pass: for every Ingest-HbStatz-enabled tournament, cross-references
// hsi.is's finished matches against HBStatz's fixtures list (matched by date + team names — the
// two sources use unrelated match IDs) and merges HBStatz's richer per-player stat lines onto the
// existing PlayerStats rows. Synchronous and admin-triggered (no queue) — this is a manual,
// low-volume action, unlike the always-on hsi.is blob-trigger pipeline.
public class TriggerHbStatzSyncFunction
{
    private readonly ITableWriter _tableWriter;
    private readonly IBlobArchiver _blobArchiver;
    private readonly IHbStatzApiClient _hbStatzClient;

    public TriggerHbStatzSyncFunction(ITableWriter tableWriter, IBlobArchiver blobArchiver, IHbStatzApiClient hbStatzClient)
    {
        _tableWriter = tableWriter;
        _blobArchiver = blobArchiver;
        _hbStatzClient = hbStatzClient;
    }

    [Function("TriggerHbStatzSync")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "hbstatz/sync")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<TriggerHbStatzSyncFunction>();
        var result = await SyncAsync(
            req.Query["tournamentId"], req.Query["round"], req.Query["matchId"], logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // round/matchId scope to a specific tournament's round or single match and — unlike the
    // default "every never-synced match" sweep — force a re-sync even if HbStatzSyncedAt is
    // already set (the admin explicitly asked for this one/this round, e.g. after HBStatz
    // corrected a stat line). Both require tournamentIdParam to resolve the right partition.
    public async Task<HbStatzSyncResult> SyncAsync(
        string? tournamentIdParam, string? round = null, string? matchId = null,
        ILogger? logger = null, CancellationToken ct = default)
    {
        var filter = string.IsNullOrWhiteSpace(tournamentIdParam)
            ? "IngestHbStatz eq true"
            : $"RowKey eq '{Escape(tournamentIdParam)}' and IngestHbStatz eq true";
        var tournaments = await _tableWriter.QueryAsync<TournamentEntity>("Tournaments", filter, ct);

        var checkedCount = 0;
        var syncedCount = 0;
        var unmatched = new List<string>();
        var failed = new List<string>();

        foreach (var tournament in tournaments)
        {
            if (!HbStatzCompetitionMap.TryResolve(tournament.CompetitionId, out var comp, out var gender))
            {
                logger?.LogWarning(
                    "No HBStatz mapping for competition {CompetitionId}; skipping tournament {TournamentId}",
                    tournament.CompetitionId, tournament.RowKey);
                continue;
            }

            IReadOnlyList<HbStatzFixture> fixtures;
            try
            {
                var seasonStartYear = int.Parse(tournament.PartitionKey.Split('-')[0]);
                var fixturesJson = await _hbStatzClient.GetFixturesJsonAsync(comp, gender, seasonStartYear, ct);
                fixtures = JsonSerializer.Deserialize<HbStatzFixturesResponse>(fixturesJson)?.Fixtures
                    ?? new List<HbStatzFixture>();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to fetch HBStatz fixtures for competition {CompetitionId}", tournament.CompetitionId);
                failed.Add($"tournament:{tournament.RowKey}");
                continue;
            }

            var matches = await ResolveMatchesAsync(tournament.RowKey, round, matchId, ct);

            foreach (var match in matches)
            {
                checkedCount++;
                try
                {
                    if (await SyncMatchAsync(match, fixtures, logger, ct))
                        syncedCount++;
                    else
                        unmatched.Add(match.RowKey);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to sync HBStatz stats for match {MatchId}", match.RowKey);
                    failed.Add(match.RowKey);
                }
            }
        }

        return new HbStatzSyncResult(checkedCount, syncedCount, unmatched, failed);
    }

    private async Task<IReadOnlyList<MatchEntity>> ResolveMatchesAsync(
        string tournamentId, string? round, string? matchId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(matchId))
        {
            // Single-match force resync — a direct point lookup, ignoring HbStatzSyncedAt.
            var match = await _tableWriter.GetAsync<MatchEntity>("Matches", tournamentId, matchId, ct);
            return match is not null && match.Status == "S" ? new[] { match } : Array.Empty<MatchEntity>();
        }

        if (!string.IsNullOrWhiteSpace(round))
        {
            // Round force resync — ignores HbStatzSyncedAt so an already-synced round can be
            // refreshed in one action rather than one match at a time.
            var roundMatches = await _tableWriter.QueryAsync<MatchEntity>(
                "Matches",
                $"PartitionKey eq '{Escape(tournamentId)}' and Status eq 'S' and Round eq '{Escape(round)}'", ct);
            return (IReadOnlyList<MatchEntity>)roundMatches;
        }

        // Default sweep: every finished match this tournament hasn't synced yet.
        var all = await _tableWriter.QueryAsync<MatchEntity>(
            "Matches", $"PartitionKey eq '{Escape(tournamentId)}' and Status eq 'S'", ct);
        return all.Where(m => m.HbStatzSyncedAt is null).ToList();
    }

    private async Task<bool> SyncMatchAsync(
        MatchEntity match, IReadOnlyList<HbStatzFixture> fixtures, ILogger? logger, CancellationToken ct)
    {
        var homeClubName = await ResolveClubNameAsync(match.HomeTeamId, ct);
        var awayClubName = await ResolveClubNameAsync(match.AwayTeamId, ct);
        if (homeClubName is null || awayClubName is null) return false;

        var fixture = HbStatzFixtureMatcher.FindMatch(fixtures, match.Date, homeClubName, awayClubName);
        if (fixture is null) return false;

        var gameJson = await _hbStatzClient.GetGameJsonAsync(fixture.GameId, ct);
        await _blobArchiver.SaveAsync($"hbstatz/matches/{match.RowKey}.json", gameJson, ct);

        var game = JsonSerializer.Deserialize<HbStatzGameResponse>(gameJson);
        if (game?.Players is null)
        {
            logger?.LogWarning(
                "HBStatz game {GameId} for match {MatchId} had no players payload", fixture.GameId, match.RowKey);
            return false;
        }

        await MergePlayerStatsAsync(match.RowKey, match.HomeTeamId, game.Players.Home, logger, ct);
        await MergePlayerStatsAsync(match.RowKey, match.AwayTeamId, game.Players.Away, logger, ct);

        match.HbStatzSyncedAt = DateTimeOffset.UtcNow;
        await _tableWriter.UpsertAsync("Matches", match, ct, TableUpdateMode.Merge);
        return true;
    }

    private async Task<string?> ResolveClubNameAsync(string teamId, CancellationToken ct)
    {
        var dash = teamId.IndexOf('-');
        var clubId = dash > 0 ? teamId[..dash] : teamId;
        var club = await _tableWriter.GetAsync<ClubEntity>("Clubs", "club", clubId, ct);
        return club?.Name;
    }

    private async Task MergePlayerStatsAsync(
        string matchId, string teamId, IReadOnlyList<HbStatzPlayerLine> lines, ILogger? logger, CancellationToken ct)
    {
        var roster = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"PartitionKey eq '{Escape(teamId)}'", ct);

        foreach (var line in lines)
        {
            var playerId = HbStatzPlayerReconciler.Resolve(roster, line);
            if (playerId is null)
            {
                logger?.LogWarning(
                    "Could not reconcile HBStatz player {Name} (#{Number}) for team {TeamId} in match {MatchId}",
                    line.Name, line.Number, teamId, matchId);
                continue;
            }

            // Fetch-then-merge, not a bare partial upsert: PlayerStatEntity's existing HSÍ
            // fields (Goals, TournamentId, Season, ...) are non-nullable, so a fresh partial
            // entity would serialize them as 0/"" and clobber the real values under Merge.
            var existing = await _tableWriter.GetAsync<PlayerStatEntity>("PlayerStats", matchId, playerId, ct);
            if (existing is null)
            {
                logger?.LogWarning(
                    "No existing PlayerStats row for player {PlayerId} in match {MatchId}; skipping HBStatz merge",
                    playerId, matchId);
                continue;
            }

            existing.HbStatzAssists = line.Assists;
            existing.HbStatzTurnovers = line.Turnovers;
            existing.HbStatzSteals = line.Steals;
            existing.HbStatzBlocks = line.Blocks;
            existing.HbStatzLegalStops = line.LegalStops;
            existing.HbStatzShots = line.Shots;
            existing.HbStatzExpectedGoals = line.Xg;
            existing.HbStatzSaves = line.GkSaves;
            existing.HbStatzShotsFaced = line.GkShotsFaced;
            existing.HbStatzSavePct = line.GkSavePct;
            existing.HbStatzExpectedSaves = line.GkXs;
            existing.HbStatzGradeTotal = line.GradeTotal;
            existing.HbStatzGradeOffense = line.GradeOffense;
            existing.HbStatzGradeDefense = line.GradeDefense;
            existing.HbStatzGradeGoalkeeping = line.GradeGoalkeeping;

            await _tableWriter.UpsertAsync("PlayerStats", existing, ct, TableUpdateMode.Merge);
        }
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
