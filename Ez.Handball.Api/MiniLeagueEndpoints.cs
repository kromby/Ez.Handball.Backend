using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public sealed record CreateMiniLeagueRequest(string? Name);

public static class MiniLeagueEndpoints
{
    private const string Base = "/api/mini-leagues";

    public static void MapMiniLeagueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Base).RequireAuthorization();

        group.MapPost("", async (
            CreateMiniLeagueRequest req, HttpContext http, ICreateMiniLeagueUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, req.Name ?? string.Empty, ct);
            return result switch
            {
                CreateMiniLeagueResult.Created c          => Results.Json(LeagueBody(c.View, userId), statusCode: StatusCodes.Status201Created),
                CreateMiniLeagueResult.ValidationError    => Results.BadRequest(new { error = "invalid_name" }),
                CreateMiniLeagueResult.NoCurrentSeason    => Results.Json(new { error = "no_current_season" }, statusCode: StatusCodes.Status409Conflict),
                _                                         => Results.Problem()
            };
        });

        group.MapGet("/{id}", async (
            string id, HttpContext http, IGetMiniLeagueUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(id, ct);
            return result switch
            {
                GetMiniLeagueResult.Found f    => Results.Ok(LeagueBody(f.View, userId)),
                GetMiniLeagueResult.NotFound   => Results.NotFound(new { error = "league_not_found" }),
                _                              => Results.Problem()
            };
        });
    }

    // role is the caller's role in the league, resolved from the members; null if the caller is not a member.
    private static object LeagueBody(MiniLeagueView view, string callerUserId) => new
    {
        id            = view.League.Id,
        name          = view.League.Name,
        season        = view.League.Season,
        creatorUserId = view.League.CreatorUserId,
        memberCount   = view.Members.Count,
        role          = view.Members.FirstOrDefault(m => m.UserId == callerUserId)?.Role,
        createdAt     = view.League.CreatedAt,
        members       = view.Members.Select(m => new
        {
            userId   = m.UserId,
            role     = m.Role,
            joinedAt = m.JoinedAt
        })
    };
}
