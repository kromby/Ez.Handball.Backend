using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

public static class ShortlistEndpoints
{
    private const string Base = "/api/users/me/shortlist";

    public static void MapShortlistEndpoints(this WebApplication app)
    {
        app.MapGet(Base, async (
            HttpContext http, IGetShortlistUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var view = await uc.ExecuteAsync(userId, ct);
            return Results.Ok(new { items = view.Items, count = view.Count, max = view.Max });
        }).RequireAuthorization();

        app.MapPut($"{Base}/{{playerId}}", async (
            string playerId, HttpContext http, IAddToShortlistUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, playerId, ct);
            return result switch
            {
                AddToShortlistResult.Added          => Results.NoContent(),
                AddToShortlistResult.AlreadyPresent => Results.NoContent(),
                AddToShortlistResult.PlayerNotFound => Results.NotFound(new { error = "player_not_found" }),
                AddToShortlistResult.CapReached c   => Results.Json(
                    new { error = "shortlist_full", details = new { max = c.Max } },
                    statusCode: StatusCodes.Status409Conflict),
                _                                   => Results.Problem()
            };
        }).RequireAuthorization();

        app.MapDelete($"{Base}/{{playerId}}", async (
            string playerId, HttpContext http, IRemoveFromShortlistUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            await uc.ExecuteAsync(userId, playerId, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
