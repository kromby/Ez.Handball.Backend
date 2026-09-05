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

public record BackfillPositionResult(
    string PlayerId, string PlayerName, string OldPosition, string NewPosition, string OldSecondary, string NewSecondary);

public record BackfillPlayerPositionsResult(
    bool DryRun, int BlobsProcessed, int PlayersUpdated, IReadOnlyList<BackfillPositionResult> Changes, IReadOnlyList<string> Errors);

// One-time (rerunnable) historical backfill: replays every archived hbstatz/matches/*.json blob
// through the same reconciliation + position mapping the live TriggerHbStatzSyncFunction uses,
// without any new HTTP calls to HBStatz. Needed because TriggerHbStatzSyncFunction's default
// sweep skips matches that already have HbStatzSyncedAt set, so matches synced before this
// feature existed would otherwise never get a Position.
public class BackfillPlayerPositionsFunction
{
    private readonly ITableWriter _tableWriter;
    private readonly IBlobArchiver _blobArchiver;

    public BackfillPlayerPositionsFunction(ITableWriter tableWriter, IBlobArchiver blobArchiver)
    {
        _tableWriter = tableWriter;
        _blobArchiver = blobArchiver;
    }

    [Function("BackfillPlayerPositions")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/backfill-positions")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<BackfillPlayerPositionsFunction>();
        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);
        var result = await ProcessAsync(dryRun, logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<BackfillPlayerPositionsResult> ProcessAsync(
        bool dryRun, ILogger? logger = null, CancellationToken ct = default)
    {
        var observationsByPlayer = new Dictionary<string, List<(string Code, DateTimeOffset MatchDate, string MatchId)>>();
        var errors = new List<string>();
        var blobsProcessed = 0;

        await foreach (var blob in _blobArchiver.ListAsync("hbstatz/matches/", ct))
        {
            if (!blob.EndsWith(".json", StringComparison.Ordinal)) continue;

            var matchId = ExtractMatchId(blob);
            try
            {
                var matches = await _tableWriter.QueryAsync<MatchEntity>("Matches", $"RowKey eq '{Escape(matchId)}'", ct);
                var match = matches.FirstOrDefault();
                if (match is null)
                {
                    errors.Add($"{blob}: no Matches row for {matchId}");
                    continue;
                }

                var json = await _blobArchiver.ReadAsync(blob, ct);
                var game = JsonSerializer.Deserialize<HbStatzGameResponse>(json);
                if (game?.Players is null)
                {
                    errors.Add($"{blob}: no players payload");
                    continue;
                }

                await TallyTeamAsync(match.HomeTeamId, match.Date, matchId, game.Players.Home, observationsByPlayer, errors, logger, ct);
                await TallyTeamAsync(match.AwayTeamId, match.Date, matchId, game.Players.Away, observationsByPlayer, errors, logger, ct);
                blobsProcessed++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Backfill failed for blob {Blob}", blob);
                errors.Add($"{blob}: {ex.Message}");
            }
        }

        var changes = new List<BackfillPositionResult>();
        foreach (var (playerId, observations) in observationsByPlayer)
        {
            var (primary, secondary) = PositionModeCalculator.Compute(
                observations.Select(o => (o.Code, o.MatchDate)).ToList());

            var players = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"RowKey eq '{Escape(playerId)}'", ct);
            var player = players.FirstOrDefault();
            if (player is null) continue;

            var newSecondary = secondary ?? string.Empty;
            var changed = player.Position != primary || player.PositionSecondary != newSecondary;
            if (changed)
            {
                changes.Add(new BackfillPositionResult(
                    playerId, player.Name, player.Position, primary, player.PositionSecondary ?? string.Empty, newSecondary));
            }

            if (!dryRun)
            {
                foreach (var (code, matchDate, matchId) in observations)
                {
                    await _tableWriter.UpsertAsync("PlayerPositionObservations", new PlayerPositionObservationEntity
                    {
                        PartitionKey = playerId, RowKey = matchId, Position = code, MatchDate = matchDate
                    }, ct);
                }

                if (changed)
                {
                    player.Position = primary;
                    player.PositionSecondary = newSecondary;
                    await _tableWriter.UpsertAsync("Players", player, ct, TableUpdateMode.Merge);
                }
            }
        }

        // A dry run reports what WOULD change, never what was applied — PlayersUpdated must
        // reflect actual writes, not the preview.
        return new BackfillPlayerPositionsResult(dryRun, blobsProcessed, dryRun ? 0 : changes.Count, changes, errors);
    }

    private async Task TallyTeamAsync(
        string teamId, DateTimeOffset matchDate, string matchId, IReadOnlyList<HbStatzPlayerLine> lines,
        Dictionary<string, List<(string Code, DateTimeOffset MatchDate, string MatchId)>> tally,
        List<string> errors, ILogger? logger, CancellationToken ct)
    {
        var roster = await _tableWriter.QueryAsync<PlayerEntity>("Players", $"PartitionKey eq '{Escape(teamId)}'", ct);
        foreach (var line in lines)
        {
            var playerId = HbStatzPlayerReconciler.Resolve(roster, line);
            if (playerId is null)
            {
                // Matches the sibling live-sync path's warning (TriggerHbStatzSyncFunction), and
                // also surfaced in Errors — a server log line is invisible to whoever called this
                // HTTP endpoint, but the response's Errors list isn't. Without this, the blob still
                // counts as "processed" while this player silently gets no observation.
                var detail = $"match {matchId}: could not reconcile HBStatz player {line.Name} (#{line.Number}) for team {teamId}";
                logger?.LogWarning(
                    "Could not reconcile HBStatz player {Name} (#{Number}) for team {TeamId} in match {MatchId}",
                    line.Name, line.Number, teamId, matchId);
                errors.Add(detail);
                continue;
            }

            // An unmapped position label isn't a reconciliation failure — the player and match
            // are known, HBStatz just used a label our vocabulary doesn't recognize (or gave
            // none). Not logged as a warning; HbStatzPositionMapper is the single place that
            // vocabulary is defined and extended.
            var code = HbStatzPositionMapper.MapToCode(line.Position);
            if (code is null) continue;

            if (!tally.TryGetValue(playerId, out var list))
            {
                list = new List<(string, DateTimeOffset, string)>();
                tally[playerId] = list;
            }
            list.Add((code, matchDate, matchId));
        }
    }

    // "hbstatz/matches/{matchId}.json"
    private static string ExtractMatchId(string blobPath)
    {
        var file = blobPath.Split('/')[^1];
        const string suffix = ".json";
        return file.EndsWith(suffix, StringComparison.Ordinal) ? file[..^suffix.Length] : file;
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
