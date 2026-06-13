using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public sealed record BuySquadPlayerRequest(
    string? PlayerId, string? Flavor, string? Season, string? TournamentId, int? RuleSetVersion);

public static class SquadEndpoints
{
    private const string Base = "/api/users/me/squad";

    public static void MapSquadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Base).RequireAuthorization();

        group.MapGet("", async (
            string? flavor,
            string? season,
            string? tournamentId,
            int? ruleSetVersion,
            HttpContext http,
            IGetSquadUseCase uc,
            CancellationToken ct) =>
        {
            // Fantasy-only: blank or "fantasy" is accepted; anything else is rejected.
            if (!IsFantasy(flavor))
                return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
            return result switch
            {
                GetSquadResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
                GetSquadResult.Found f         => Results.Ok(SquadBody(f.View)),
                _                              => Results.Problem()
            };
        });

        group.MapPost("/players", async (
            BuySquadPlayerRequest req, HttpContext http, IBuyPlayerUseCase uc, IGetCurrentGameweekUseCase gw, CancellationToken ct) =>
        {
            if (!IsFantasy(req.Flavor)) return Results.BadRequest(new { error = "invalid_flavor" });
            if (string.IsNullOrWhiteSpace(req.PlayerId)) return Results.BadRequest(new { error = "invalid_player_id" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId,
                req.PlayerId, new BuyPlayerContext(req.Season, req.TournamentId, req.RuleSetVersion), ct);
            if (result is BuyPlayerResult.Committed c)
            {
                var gameweek = await GameweekEcho.BuildAsync(gw, ct);
                return Results.Json(new { squad = SquadBody(c.View), gameweek }, statusCode: StatusCodes.Status201Created);
            }
            return result switch
            {
                BuyPlayerResult.Rejected r      => Results.Json(new { violations = r.Violations }, statusCode: StatusCodes.Status422UnprocessableEntity),
                BuyPlayerResult.Duplicate       => Results.Json(new { error = "duplicate_player" }, statusCode: StatusCodes.Status409Conflict),
                BuyPlayerResult.NoTeam          => Results.Json(new { error = "no_team" }, statusCode: StatusCodes.Status409Conflict),
                BuyPlayerResult.PlayerNotFound  => Results.NotFound(new { error = "player_not_found" }),
                BuyPlayerResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
                _                               => Results.Problem()
            };
        });

        group.MapDelete("/players/{playerId}", async (
            string playerId, string? flavor, string? season, string? tournamentId, int? ruleSetVersion,
            HttpContext http, ISellPlayerUseCase uc, IGetCurrentGameweekUseCase gw, CancellationToken ct) =>
        {
            if (!IsFantasy(flavor)) return Results.BadRequest(new { error = "invalid_flavor" });
            if (string.IsNullOrWhiteSpace(playerId)) return Results.BadRequest(new { error = "invalid_player_id" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId,
                playerId, new BuyPlayerContext(season, tournamentId, ruleSetVersion), ct);
            if (result is SellPlayerResult.Sold s)
            {
                var gameweek = await GameweekEcho.BuildAsync(gw, ct);
                return Results.Ok(new { squad = SquadBody(s.View), gameweek });
            }
            return result switch
            {
                SellPlayerResult.NotInSquad      => Results.NotFound(new { error = "not_in_squad" }),
                SellPlayerResult.NoTeam          => Results.Json(new { error = "no_team" }, statusCode: StatusCodes.Status409Conflict),
                SellPlayerResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
                _                                => Results.Problem()
            };
        });
    }

    private static bool IsFantasy(string? flavor)
        => string.IsNullOrWhiteSpace(flavor) || flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase);

    private static object SquadBody(SquadView view) => new
    {
        flavor = "fantasy",
        players = view.Players.Select(p => new
        {
            playerId = p.PlayerId,
            name = p.Name,
            clubId = p.ClubId,
            clubName = p.ClubName,
            position = p.Position,
            gender = p.Gender,
            price = p.Price,
            pricePaid = p.PricePaid,
            rating = p.Rating
        }),
        budgetUsed = view.BudgetUsed,
        remainingBudget = view.RemainingBudget,
        squadValue = view.SquadValue
    };
}
