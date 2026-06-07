using System.Globalization;
using System.Net;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ez.Handball.Ingestion.Functions;

// One-time, idempotent migration from #54's userId-keyed Squads table to the #55
// GameTeam/GameRoster/GameBudget model with a stored balance. Skips users who already have a
// GameTeam. Budget is seeded to each user's current remaining budget (StartingCap minus the
// sum of ACTIVE prices paid), preserving present state.
public class MigrateSquadToGameTeamFunction
{
    private const string Flavor = "fantasy";

    private readonly ITableWriter _tables;

    public MigrateSquadToGameTeamFunction(ITableWriter tables) => _tables = tables;

    [Function("MigrateSquadToGameTeam")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "migrate/squad-to-team")] HttpRequestData req,
        FunctionContext context)
    {
        var migrated = await ProcessAsync(context.CancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { migrated });
        return response;
    }

    public async Task<int> ProcessAsync(CancellationToken ct)
    {
        var startingCap = await ReadStartingCapAsync(ct);
        var users = await _tables.QueryAsync<UserEntity>("Users", "PartitionKey eq 'user'", ct);

        var migrated = 0;
        foreach (var user in users)
        {
            var userId = user.RowKey;
            var teamId = $"{userId}:{Flavor}";

            var existing = await _tables.GetAsync<GameTeamEntity>("GameTeams", userId, Flavor, ct);
            if (existing is not null) continue; // idempotent

            var now = DateTimeOffset.UtcNow;
            var squadRows = await _tables.QueryAsync<SquadEntryEntity>(
                "Squads", $"PartitionKey eq '{userId.Replace("'", "''")}'", ct);

            double activePaid = 0;
            foreach (var row in squadRows)
            {
                if (row.DeletedAt is null) activePaid += row.PricePaidAmount;
                await _tables.UpsertAsync("GameRosters", new GameRosterEntity
                {
                    PartitionKey = teamId,
                    RowKey = row.RowKey,
                    Position = row.Position,
                    PricePaidAmount = row.PricePaidAmount,
                    CreatedAt = row.CreatedAt,
                    DeletedAt = row.DeletedAt
                }, ct);
            }

            await _tables.UpsertAsync("GameTeams", new GameTeamEntity
            {
                PartitionKey = userId, RowKey = Flavor, TeamId = teamId, Name = "My Team", CreatedAt = now
            }, ct);

            await _tables.UpsertAsync("GameBudgets", new GameBudgetEntity
            {
                PartitionKey = teamId, RowKey = "balance", Amount = startingCap - activePaid, UpdatedAt = now
            }, ct);

            migrated++;
        }

        return migrated;
    }

    private async Task<double> ReadStartingCapAsync(CancellationToken ct)
    {
        var cap = await _tables.GetAsync<ConfigEntity>("Config", "fantasy-squad-v1", "startingCap", ct);
        return cap is not null && double.TryParse(cap.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
