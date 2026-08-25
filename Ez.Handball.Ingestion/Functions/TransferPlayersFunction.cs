using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

// One request per player move for a transfer window. "Transfer" relocates an existing
// player row to a new club's partition (Table Storage can't change PartitionKey in place,
// so this is an upsert-then-delete). "Retire" just sets the existing Retired flag — same
// convention as BootstrapRetiredFunction/CLAUDE.md's "curate the Retired column" note.
// "Create" is for players arriving from a club this system has never ingested (no hsi.is
// playerId yet); it seeds a placeholder row that a later hsi.is-driven parse can reconcile.
// ToClub names that don't match an existing Clubs row are created on the fly (e.g. a single
// synthetic "Erlendis" club to park players who left for a foreign club without modelling
// every foreign team).
public record TransferRequest(string PlayerName, string? FromClub, string? ToClub, string Action, string? Gender);

public record TransferResult(string PlayerName, string Action, string Status, string? Detail);

public record TransferBatchResult(bool DryRun, IReadOnlyList<TransferResult> Results);

public class TransferPlayersFunction
{
    private const string DefaultGender = "karlar";

    private readonly ITableWriter _tableWriter;

    public TransferPlayersFunction(ITableWriter tableWriter)
    {
        _tableWriter = tableWriter;
    }

    [Function("TransferPlayers")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "players/transfer")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger<TransferPlayersFunction>();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var requests = await JsonSerializer.DeserializeAsync<List<TransferRequest>>(
            req.Body, options, context.CancellationToken) ?? [];

        // Defaults to a dry run — a caller must pass ?dryRun=false to actually write.
        var dryRun = !string.Equals(req.Query["dryRun"], "false", StringComparison.OrdinalIgnoreCase);

        var result = await ProcessAsync(requests, dryRun, logger, context.CancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    public async Task<TransferBatchResult> ProcessAsync(
        IReadOnlyList<TransferRequest> requests, bool dryRun, ILogger? logger = null, CancellationToken ct = default)
    {
        var clubsByName = (await _tableWriter.QueryAsync<ClubEntity>("Clubs", null!, ct))
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var results = new List<TransferResult>();

        foreach (var request in requests)
        {
            var action = request.Action.Trim().ToLowerInvariant();
            var gender = string.IsNullOrWhiteSpace(request.Gender) ? DefaultGender : request.Gender.Trim();

            try
            {
                results.Add(action switch
                {
                    "transfer" => await TransferAsync(request, gender, clubsByName, dryRun, ct),
                    "retire" => await RetireAsync(request, dryRun, ct),
                    "create" => await CreateAsync(request, gender, clubsByName, dryRun, ct),
                    _ => new TransferResult(request.PlayerName, request.Action, "UnknownAction",
                        "Action must be 'transfer', 'retire', or 'create'.")
                });
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Transfer request failed for {Player}", request.PlayerName);
                results.Add(new TransferResult(request.PlayerName, request.Action, "Error", ex.Message));
            }
        }

        return new TransferBatchResult(dryRun, results);
    }

    private async Task<TransferResult> TransferAsync(
        TransferRequest request, string gender, Dictionary<string, ClubEntity> clubsByName, bool dryRun, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToClub))
            return new TransferResult(request.PlayerName, request.Action, "MissingToClub", null);

        var found = await FindPlayerAsync(request.PlayerName, request.FromClub, clubsByName, ct);
        if (found.Player is null)
            return new TransferResult(request.PlayerName, request.Action, found.StatusCode!, found.Detail);
        var player = found.Player;

        var toClub = await ResolveOrCreateClubAsync(request.ToClub, clubsByName, dryRun, ct);
        var newPartitionKey = $"{toClub.RowKey}-{gender}";

        if (newPartitionKey == player.PartitionKey)
        {
            return new TransferResult(request.PlayerName, request.Action, "AlreadyAtClub",
                $"{player.Name} is already in partition {newPartitionKey}.");
        }

        var detail = $"{player.Name}: {player.ClubName} ({player.PartitionKey}) -> {toClub.Name} ({newPartitionKey})";

        if (dryRun) return new TransferResult(request.PlayerName, request.Action, "DryRun", detail);

        await _tableWriter.UpsertAsync("Teams", new TeamEntity
        {
            RowKey = newPartitionKey,
            ClubId = toClub.RowKey,
            Gender = gender,
            Name = toClub.Name
        }, ct);

        await _tableWriter.UpsertAsync("Players", new PlayerEntity
        {
            PartitionKey = newPartitionKey,
            RowKey = player.RowKey,
            Name = player.Name,
            Position = player.Position,
            JerseyNumber = player.JerseyNumber,
            DateOfBirth = player.DateOfBirth,
            Gender = gender,
            ClubId = toClub.RowKey,
            ClubName = toClub.Name,
            Retired = false
        }, ct);

        await _tableWriter.DeleteAsync("Players", player.PartitionKey, player.RowKey, ct);

        return new TransferResult(request.PlayerName, request.Action, "Applied", detail);
    }

    private async Task<TransferResult> RetireAsync(TransferRequest request, bool dryRun, CancellationToken ct)
    {
        var found = await FindPlayerAsync(request.PlayerName, request.FromClub, null, ct);
        if (found.Player is null)
            return new TransferResult(request.PlayerName, request.Action, found.StatusCode!, found.Detail);
        var player = found.Player;

        var detail = $"{player.Name} ({player.ClubName})";
        if (dryRun) return new TransferResult(request.PlayerName, request.Action, "DryRun", detail);

        player.Retired = true;
        await _tableWriter.UpsertAsync("Players", player, ct, TableUpdateMode.Merge);

        return new TransferResult(request.PlayerName, request.Action, "Applied", detail);
    }

    private async Task<TransferResult> CreateAsync(
        TransferRequest request, string gender, Dictionary<string, ClubEntity> clubsByName, bool dryRun, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToClub))
            return new TransferResult(request.PlayerName, request.Action, "MissingToClub", null);

        var toClub = await ResolveOrCreateClubAsync(request.ToClub, clubsByName, dryRun, ct);
        var partitionKey = $"{toClub.RowKey}-{gender}";

        var existing = await _tableWriter.QueryAsync<PlayerEntity>(
            "Players", $"ClubId eq '{Escape(toClub.RowKey)}'", ct);
        if (existing.Any(p => string.Equals(p.Name.Trim(), request.PlayerName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return new TransferResult(request.PlayerName, request.Action, "AlreadyExists",
                $"{request.PlayerName} already has a row under {toClub.Name}.");
        }

        var detail = $"{request.PlayerName} -> {toClub.Name} ({partitionKey}) [placeholder]";
        if (dryRun) return new TransferResult(request.PlayerName, request.Action, "DryRun", detail);

        await _tableWriter.UpsertAsync("Teams", new TeamEntity
        {
            RowKey = partitionKey,
            ClubId = toClub.RowKey,
            Gender = gender,
            Name = toClub.Name
        }, ct);

        await _tableWriter.UpsertAsync("Players", new PlayerEntity
        {
            PartitionKey = partitionKey,
            RowKey = $"placeholder-{Guid.NewGuid():N}",
            Name = request.PlayerName.Trim(),
            Position = string.Empty,
            Gender = gender,
            ClubId = toClub.RowKey,
            ClubName = toClub.Name,
            Retired = false
        }, ct);

        return new TransferResult(request.PlayerName, request.Action, "Applied", detail);
    }

    private async Task<(PlayerEntity? Player, string? StatusCode, string? Detail)> FindPlayerAsync(
        string playerName, string? fromClub, Dictionary<string, ClubEntity>? clubsByName, CancellationToken ct)
    {
        IList<PlayerEntity> candidates;
        if (!string.IsNullOrWhiteSpace(fromClub))
        {
            ClubEntity? club = null;
            if (clubsByName is not null) clubsByName.TryGetValue(fromClub.Trim(), out club);
            club ??= (await _tableWriter.QueryAsync<ClubEntity>("Clubs", null!, ct))
                .FirstOrDefault(c => string.Equals(c.Name.Trim(), fromClub.Trim(), StringComparison.OrdinalIgnoreCase));

            if (club is null)
                return (null, "FromClubNotFound", fromClub);

            candidates = await _tableWriter.QueryAsync<PlayerEntity>(
                "Players", $"ClubId eq '{Escape(club.RowKey)}'", ct);
        }
        else
        {
            candidates = await _tableWriter.QueryAsync<PlayerEntity>("Players", null!, ct);
        }

        var matches = candidates
            .Where(p => string.Equals(p.Name.Trim(), playerName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => (null, "PlayerNotFound", fromClub),
            > 1 => (null, "MultiplePlayersMatched", string.Join(", ", matches.Select(m => $"{m.PartitionKey}/{m.RowKey}"))),
            _ => (matches[0], null, null)
        };
    }

    private async Task<ClubEntity> ResolveOrCreateClubAsync(
        string name, Dictionary<string, ClubEntity> clubsByName, bool dryRun, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (clubsByName.TryGetValue(trimmed, out var existing)) return existing;

        var club = new ClubEntity { RowKey = $"custom-{Guid.NewGuid():N}", Name = trimmed };
        clubsByName[trimmed] = club;

        if (!dryRun) await _tableWriter.UpsertAsync("Clubs", club, ct, TableUpdateMode.Merge);

        return club;
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
