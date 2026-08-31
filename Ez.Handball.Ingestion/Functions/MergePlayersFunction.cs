using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

// hsi.is occasionally splits one real player across two different playerIds (usually across a
// season boundary). This merges MergePlayerId's history into KeepPlayerId: re-keys every
// PlayerStats row, backfills any blank fields on the surviving Players row, then deletes the
// loser's Players row. A playerId is also referenced (as a RowKey, or embedded in one) by seven
// fantasy-game tables — GameRosters, Squads, Shortlists, GameLineups, GameweekLineups,
// GameweekScores, GameTransferLedger — so before touching anything this checks all of them for
// MergePlayerId and refuses the merge if it finds a reference there, since migrating fantasy
// history (frozen prices, settled scores, an audit ledger with the id embedded in its RowKey)
// is a different, much riskier operation than this endpoint performs.
public record MergePlayersRequest(string KeepPlayerId, string MergePlayerId, string? PreferredName);

public record MergePlayersResult(string KeepPlayerId, string MergePlayerId, string Status, string? Detail);

public record MergeBatchResult(bool DryRun, IReadOnlyList<MergePlayersResult> Results);

public class MergePlayersFunction
{
    private readonly ITableWriter _tableWriter;

    public MergePlayersFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("MergePlayers")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/merge")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<MergePlayersFunction>();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var requests = await JsonSerializer.DeserializeAsync<List<MergePlayersRequest>>(
            req.Body, options, context.CancellationToken) ?? [];

        // Defaults to a dry run — a caller must pass ?dryRun=false to actually write.
        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);

        var result = await ProcessAsync(requests, dryRun, logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<MergeBatchResult> ProcessAsync(
        IReadOnlyList<MergePlayersRequest> requests, bool dryRun, ILogger? logger = null, CancellationToken ct = default)
    {
        var results = new List<MergePlayersResult>();

        foreach (var request in requests)
        {
            try
            {
                results.Add(await MergeAsync(request, dryRun, ct));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Merge failed for keep={Keep} merge={Merge}", request.KeepPlayerId, request.MergePlayerId);
                results.Add(new MergePlayersResult(request.KeepPlayerId, request.MergePlayerId, "Error", ex.Message));
            }
        }

        return new MergeBatchResult(dryRun, results);
    }

    private async Task<MergePlayersResult> MergeAsync(MergePlayersRequest request, bool dryRun, CancellationToken ct)
    {
        if (request.KeepPlayerId == request.MergePlayerId)
            return new MergePlayersResult(request.KeepPlayerId, request.MergePlayerId, "SameId", null);

        var keepPlayer = await FindPlayerAsync(request.KeepPlayerId, ct);
        var mergePlayer = await FindPlayerAsync(request.MergePlayerId, ct);

        if (keepPlayer is null)
            return new MergePlayersResult(request.KeepPlayerId, request.MergePlayerId, "KeepPlayerNotFound", null);
        if (mergePlayer is null)
            return new MergePlayersResult(request.KeepPlayerId, request.MergePlayerId, "MergePlayerNotFound", null);

        var blocker = await FindFantasyReferenceAsync(request.MergePlayerId, ct);
        if (blocker is not null)
        {
            return new MergePlayersResult(request.KeepPlayerId, request.MergePlayerId, "BlockedReferencedElsewhere", blocker);
        }

        var stats = await _tableWriter.QueryAsync<PlayerStatEntity>(
            "PlayerStats", $"RowKey eq '{Escape(request.MergePlayerId)}'", ct);

        var detail = $"{mergePlayer.Name} ({request.MergePlayerId}, {stats.Count} stat rows) -> {keepPlayer.Name} ({request.KeepPlayerId})";
        if (dryRun) return new MergePlayersResult(request.KeepPlayerId, request.MergePlayerId, "DryRun", detail);

        foreach (var stat in stats)
        {
            await _tableWriter.UpsertAsync("PlayerStats", new PlayerStatEntity
            {
                PartitionKey = stat.PartitionKey,
                RowKey = request.KeepPlayerId,
                Goals = stat.Goals,
                YellowCards = stat.YellowCards,
                TwoMinuteSuspensions = stat.TwoMinuteSuspensions,
                RedCards = stat.RedCards,
                TournamentId = stat.TournamentId,
                Season = stat.Season,
                TeamId = stat.TeamId,
                ClubName = stat.ClubName
            }, ct);

            await _tableWriter.DeleteAsync("PlayerStats", stat.PartitionKey, request.MergePlayerId, ct);
        }

        keepPlayer.Name = string.IsNullOrWhiteSpace(request.PreferredName) ? keepPlayer.Name : request.PreferredName.Trim();
        keepPlayer.DateOfBirth ??= mergePlayer.DateOfBirth;
        keepPlayer.JerseyNumber ??= mergePlayer.JerseyNumber;
        if (string.IsNullOrWhiteSpace(keepPlayer.Position)) keepPlayer.Position = mergePlayer.Position;
        keepPlayer.ClubName ??= mergePlayer.ClubName;

        await _tableWriter.UpsertAsync("Players", keepPlayer, ct);
        await _tableWriter.DeleteAsync("Players", mergePlayer.PartitionKey, mergePlayer.RowKey, ct);

        return new MergePlayersResult(request.KeepPlayerId, request.MergePlayerId, "Applied", detail);
    }

    private async Task<PlayerEntity?> FindPlayerAsync(string playerId, CancellationToken ct)
    {
        var matches = await _tableWriter.QueryAsync<PlayerEntity>(
            "Players", $"RowKey eq '{Escape(playerId)}'", ct);
        return matches.FirstOrDefault();
    }

    // A playerId is a RowKey (or embedded in one) on seven fantasy-game tables. None of them are
    // safe to blindly re-key here — a frozen historical price, a settled gameweek score, or an
    // audit-ledger row needs its own migration, not a silent merge. Report the first hit found.
    private async Task<string?> FindFantasyReferenceAsync(string playerId, CancellationToken ct)
    {
        string[] rowKeyTables = ["GameRosters", "Squads", "Shortlists", "GameLineups", "GameweekLineups"];
        foreach (var table in rowKeyTables)
        {
            var hits = await _tableWriter.QueryAsync<TableEntity>(table, $"RowKey eq '{Escape(playerId)}'", ct);
            if (hits.Count > 0) return $"{table} has {hits.Count} row(s) keyed by this playerId";
        }

        var ledgerHits = await _tableWriter.QueryAsync<TableEntity>(
            "GameTransferLedger", $"PlayerId eq '{Escape(playerId)}'", ct);
        if (ledgerHits.Count > 0) return $"GameTransferLedger has {ledgerHits.Count} row(s) for this playerId";

        // GameweekScores embeds playerIds inside a JSON blob (BreakdownJson) and a plain
        // CaptainPlayerId column — neither is filterable server-side, so scan client-side.
        var scores = await _tableWriter.QueryAsync<TableEntity>("GameweekScores", null!, ct);
        foreach (var score in scores)
        {
            var captainId = score.GetString("CaptainPlayerId");
            var breakdown = score.GetString("BreakdownJson");
            if (captainId == playerId || (breakdown is not null && breakdown.Contains($"\"{playerId}\"")))
                return "GameweekScores has a settled score referencing this playerId";
        }

        return null;
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
