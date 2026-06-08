using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

public sealed record GenerateInviteRequest(int? ExpiresInDays);
public sealed record JoinMiniLeagueRequest(string? Token);

public static class MiniLeagueInviteEndpoints
{
    public static void MapMiniLeagueInviteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mini-leagues").RequireAuthorization();

        group.MapPost("/{id}/invite", async (
            string id, GenerateInviteRequest? req, HttpContext http, IGenerateInviteUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, id, req?.ExpiresInDays, ct);
            return result switch
            {
                GenerateInviteResult.Generated g    => Results.Json(new { token = g.Token, expiresAt = g.ExpiresAt }, statusCode: StatusCodes.Status201Created),
                GenerateInviteResult.LeagueNotFound => Results.NotFound(new { error = "league_not_found" }),
                GenerateInviteResult.NotMember      => Results.Json(new { error = "not_member" }, statusCode: StatusCodes.Status403Forbidden),
                GenerateInviteResult.InvalidExpiry  => Results.BadRequest(new { error = "invalid_expiry" }),
                _                                   => Results.Problem()
            };
        });

        group.MapGet("/{id}/invite", async (
            string id, HttpContext http, IGetInviteUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, id, ct);
            return result switch
            {
                GetInviteResult.Found f        => Results.Ok(new { token = f.Token, expiresAt = f.ExpiresAt }),
                GetInviteResult.NoInvite       => Results.NotFound(new { error = "no_invite" }),
                GetInviteResult.LeagueNotFound => Results.NotFound(new { error = "league_not_found" }),
                GetInviteResult.NotMember      => Results.Json(new { error = "not_member" }, statusCode: StatusCodes.Status403Forbidden),
                _                              => Results.Problem()
            };
        });

        group.MapGet("/invite/{token}", async (
            string token, IPreviewInviteUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(token, ct);
            return result switch
            {
                PreviewInviteResult.Found f       => Results.Ok(new { leagueId = f.LeagueId, name = f.Name, season = f.Season, memberCount = f.MemberCount }),
                PreviewInviteResult.InvalidInvite => Results.NotFound(new { error = "invalid_invite" }),
                PreviewInviteResult.InviteExpired => Results.Json(new { error = "invite_expired" }, statusCode: StatusCodes.Status410Gone),
                _                                 => Results.Problem()
            };
        });

        group.MapPost("/join", async (
            JoinMiniLeagueRequest req, HttpContext http, IJoinMiniLeagueUseCase uc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Token))
                return Results.BadRequest(new { error = "invalid_token" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, req.Token, ct);
            return result switch
            {
                JoinMiniLeagueResult.Joined j        => Results.Ok(MiniLeagueResponse.Body(j.View, userId)),
                JoinMiniLeagueResult.AlreadyMember a => Results.Ok(MiniLeagueResponse.Body(a.View, userId)),
                JoinMiniLeagueResult.InvalidInvite   => Results.NotFound(new { error = "invalid_invite" }),
                JoinMiniLeagueResult.InviteExpired   => Results.Json(new { error = "invite_expired" }, statusCode: StatusCodes.Status410Gone),
                _                                    => Results.Problem()
            };
        });
    }
}
