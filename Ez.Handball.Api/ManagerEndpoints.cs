using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

public sealed record RenameTeamRequest(string? TeamName);

public static class ManagerEndpoints
{
    private const string Base = "/api/users/me/manager";

    public static void MapManagerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Base).RequireAuthorization();

        group.MapGet("", async (
            string? flavor, int? ruleSetVersion, HttpContext http, IGetManagerUseCase uc, CancellationToken ct) =>
        {
            if (!IsFantasy(flavor)) return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, ruleSetVersion, ct);
            return result switch
            {
                GetManagerResult.Found f         => Results.Ok(ManagerBody(f.View)),
                GetManagerResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
                GetManagerResult.NoTeam          => Results.NotFound(new { error = "no_team" }),
                _                                => Results.Problem()
            };
        });

        group.MapPatch("", async (
            RenameTeamRequest req, string? flavor, HttpContext http, IRenameTeamUseCase uc, CancellationToken ct) =>
        {
            if (!IsFantasy(flavor)) return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, req.TeamName ?? string.Empty, ct);
            return result switch
            {
                RenameTeamResult.Success s          => Results.Ok(ManagerBody(s.View)),
                RenameTeamResult.ValidationError v  => Results.BadRequest(new { error = "validation_error", details = new { field = v.Field } }),
                RenameTeamResult.TeamNameTaken      => Results.Conflict(new { error = "team_name_taken" }),
                RenameTeamResult.RuleSetNotFound    => Results.BadRequest(new { error = "invalid_rule_set" }),
                RenameTeamResult.NoTeam             => Results.NotFound(new { error = "no_team" }),
                _                                   => Results.Problem()
            };
        });
    }

    private static bool IsFantasy(string? flavor)
        => string.IsNullOrWhiteSpace(flavor) || flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase);

    private static object ManagerBody(ManagerView view) => new
    {
        flavor = "fantasy",
        teamName = view.TeamName,
        favoriteClubId = view.FavoriteClubId,
        color = view.Color,
        onboarding = new
        {
            squadComplete = view.Onboarding.SquadComplete,
            playersOwned = view.Onboarding.PlayersOwned,
            squadSize = view.Onboarding.SquadSize
        }
    };
}
