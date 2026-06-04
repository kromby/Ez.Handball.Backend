using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

public static class ShortlistEndpoints
{
    private const string Base = "/api/users/me/shortlist";

    public static void MapShortlistEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Base).RequireAuthorization();

        group.MapGet("", async (HttpContext http, IGetShortlistUseCase uc, CancellationToken ct) =>
        {
            var userId = AuthenticatedUserId(http, out var unauthorized);
            if (userId is null) return unauthorized!;

            var view = await uc.ExecuteAsync(userId, ct);
            return Results.Ok(new { items = view.Items, count = view.Count, max = view.Max });
        });

        group.MapPut("/{playerId}", async (
            string playerId, HttpContext http, IAddToShortlistUseCase uc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return Results.BadRequest(new { error = "invalid_player_id" });

            var userId = AuthenticatedUserId(http, out var unauthorized);
            if (userId is null) return unauthorized!;

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
        });

        group.MapDelete("/{playerId}", async (
            string playerId, HttpContext http, IRemoveFromShortlistUseCase uc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return Results.BadRequest(new { error = "invalid_player_id" });

            var userId = AuthenticatedUserId(http, out var unauthorized);
            if (userId is null) return unauthorized!;

            await uc.ExecuteAsync(userId, playerId, ct);
            return Results.NoContent();
        });
    }

    // Returns the authenticated user id, or null after writing a 401 to `unauthorized`.
    private static string? AuthenticatedUserId(HttpContext http, out IResult? unauthorized)
    {
        var userId = http.User.UserId();
        if (string.IsNullOrEmpty(userId))
        {
            unauthorized = Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
            return null;
        }
        unauthorized = null;
        return userId;
    }
}
