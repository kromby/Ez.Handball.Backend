using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

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
            if (!string.IsNullOrWhiteSpace(flavor)
                && !flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
            return result switch
            {
                GetSquadResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
                GetSquadResult.Found f => Results.Ok(new
                {
                    flavor = "fantasy",
                    players = f.View.Players.Select(p => new
                    {
                        playerId = p.PlayerId,
                        name = p.Name,
                        clubId = p.ClubId,
                        clubName = p.ClubName,
                        position = p.Position,
                        gender = p.Gender,
                        price = p.Price,
                        pricePaid = p.PricePaid
                    }),
                    budgetUsed = f.View.BudgetUsed,
                    remainingBudget = f.View.RemainingBudget,
                    squadValue = f.View.SquadValue
                }),
                _ => Results.Problem()
            };
        });
    }
}
