using System.Net;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

public record DedupedPlayer(
    string PlayerId, string PlayerName, string KeptClubName, string KeptPartitionKey,
    IReadOnlyList<string> RemovedPartitionKeys);

public record DedupePlayerRowsResult(bool DryRun, int PlayersWithDuplicates, int RowsRemoved, IReadOnlyList<DedupedPlayer> Changes);

// One-time (rerunnable) cleanup for players who transferred clubs before PlayerParser started
// auto-deleting the old club's row on transfer. Table Storage can't rename a
// PartitionKey in place, so every club change before that fix left the old row behind under its
// old partition, sitting there forever with whatever position hsi.is/HBStatz last wrote for it —
// often the "Leikmaður" placeholder. Every reader that resolves a player by RowKey picks whichever
// duplicate it happens to land on, which is why the public /players pool could show a stale
// position for a player whose current club row was already correct.
//
// Heuristic: within a set of same-RowKey rows, the one with the latest Timestamp is the one still
// receiving writes from ingestion (matches keep getting parsed for a player's current club, so its
// row keeps getting re-upserted; an old club's row goes untouched the moment the player leaves).
// That row is kept and every other row for that playerId is deleted — except that the surviving
// row can itself still be carrying hsi.is's "Leikmaður" placeholder if HBStatz hasn't enriched the
// player's new club yet, while a stale row already has a real position from before the transfer.
// In that case the real position (and secondary) is carried forward onto the surviving row before
// its stale sources are deleted, so a dedupe never regresses a known position to a placeholder.
public class DedupePlayerRowsFunction
{
    private const string PlaceholderPosition = "Leikmaður";

    private readonly ITableWriter _tableWriter;

    public DedupePlayerRowsFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("DedupePlayerRows")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/dedupe")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<DedupePlayerRowsFunction>();

        // Defaults to a dry run — a caller must pass ?dryRun=false to actually delete rows.
        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);

        var result = await ProcessAsync(dryRun, logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<DedupePlayerRowsResult> ProcessAsync(bool dryRun, ILogger? logger = null, CancellationToken ct = default)
    {
        var players = await _tableWriter.QueryAsync<PlayerEntity>("Players", null!, ct);
        var changes = new List<DedupedPlayer>();
        var rowsRemoved = 0;

        foreach (var group in players.GroupBy(p => p.RowKey))
        {
            var rows = group.OrderByDescending(p => p.Timestamp).ToList();
            if (rows.Count < 2) continue;

            var keep = rows[0];
            var remove = rows.Skip(1).ToList();

            var keepHasRealPosition = !string.IsNullOrWhiteSpace(keep.Position) && keep.Position != PlaceholderPosition;
            if (!keepHasRealPosition)
            {
                var betterSource = remove.FirstOrDefault(
                    r => !string.IsNullOrWhiteSpace(r.Position) && r.Position != PlaceholderPosition);
                if (betterSource is not null)
                {
                    keep.Position = betterSource.Position;
                    keep.PositionSecondary ??= betterSource.PositionSecondary;
                    if (!dryRun)
                    {
                        await _tableWriter.UpsertAsync("Players", keep, ct, TableUpdateMode.Merge);
                    }
                }
            }

            if (!dryRun)
            {
                foreach (var stale in remove)
                {
                    await _tableWriter.DeleteAsync("Players", stale.PartitionKey, stale.RowKey, ct);
                }
            }

            changes.Add(new DedupedPlayer(
                keep.RowKey, keep.Name, keep.ClubName ?? string.Empty, keep.PartitionKey,
                remove.Select(r => r.PartitionKey).ToList()));
            rowsRemoved += remove.Count;
        }

        logger?.LogInformation(
            "Dedupe-player-rows complete: dryRun={DryRun}, playersWithDuplicates={Count}, rowsRemoved={Removed}",
            dryRun, changes.Count, rowsRemoved);

        return new DedupePlayerRowsResult(dryRun, changes.Count, rowsRemoved, changes);
    }
}
