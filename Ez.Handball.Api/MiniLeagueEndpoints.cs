using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public sealed record CreateMiniLeagueRequest(string? Name, string? StartRound = null);

// StartRound null/blank resets the league to count every round.
public sealed record SetMiniLeagueStartRoundRequest(string? StartRound);

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

            var result = await uc.ExecuteAsync(userId, req.Name ?? string.Empty, req.StartRound, ct);
            return result switch
            {
                CreateMiniLeagueResult.Created c          => Results.Json(MiniLeagueResponse.Body(c.View, userId), statusCode: StatusCodes.Status201Created),
                CreateMiniLeagueResult.ValidationError { Field: "startRound" }
                                                          => Results.BadRequest(new { error = "invalid_start_round" }),
                CreateMiniLeagueResult.ValidationError    => Results.BadRequest(new { error = "invalid_name" }),
                CreateMiniLeagueResult.NoCurrentSeason    => Results.Json(new { error = "no_current_season" }, statusCode: StatusCodes.Status409Conflict),
                _                                         => Results.Problem()
            };
        });

        group.MapGet("/mine", async (
            HttpContext http, IGetMyMiniLeaguesUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var leagues = await uc.ExecuteAsync(userId, ct);
            return Results.Ok(leagues.Select(l => new
            {
                id          = l.League.Id,
                name        = l.League.Name,
                season      = l.League.Season,
                startRound  = l.League.StartRound,
                role        = l.Role,
                memberCount = l.MemberCount
            }));
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
                GetMiniLeagueResult.Found f    => Results.Ok(MiniLeagueResponse.Body(f.View, userId)),
                GetMiniLeagueResult.NotFound   => Results.NotFound(new { error = "league_not_found" }),
                _                              => Results.Problem()
            };
        });

        group.MapPut("/{id}/start-round", async (
            string id, SetMiniLeagueStartRoundRequest req, HttpContext http,
            ISetMiniLeagueStartRoundUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, id, req.StartRound, ct);
            return result switch
            {
                SetMiniLeagueStartRoundResult.Updated u         => Results.Ok(new { id = u.League.Id, startRound = u.League.StartRound }),
                SetMiniLeagueStartRoundResult.NotFound          => Results.NotFound(new { error = "league_not_found" }),
                SetMiniLeagueStartRoundResult.NotCreator        => Results.Json(new { error = "not_creator" }, statusCode: StatusCodes.Status403Forbidden),
                SetMiniLeagueStartRoundResult.ValidationError   => Results.BadRequest(new { error = "invalid_start_round" }),
                _                                               => Results.Problem()
            };
        });

        group.MapGet("/{id}/standings", async (
            string id, int? offset, int? limit, IGetMiniLeagueStandingsUseCase uc, CancellationToken ct) =>
        {
            var off = offset ?? 0;
            var lim = limit ?? 50;
            if (off < 0 || lim < 1 || lim > 200)
                return Results.BadRequest(new { error = "invalid_pagination" });

            var result = await uc.ExecuteAsync(id, off, lim, ct);
            return result switch
            {
                GetMiniLeagueStandingsResult.Found f  => Results.Ok(f.Standings),
                GetMiniLeagueStandingsResult.NotFound => Results.NotFound(new { error = "league_not_found" }),
                _                                     => Results.Problem()
            };
        });

        // A member's team for rounds whose deadline has passed (Backend#156). 404 unless both the
        // caller and the target are members, so it never reveals whether a league or user exists.
        group.MapGet("/{id}/members/{userId}/gameweeks", async (
            string id, string userId, int? version, HttpContext http,
            IGetLeagueMemberGameweeksUseCase uc, CancellationToken ct) =>
        {
            var callerId = http.User.UserId();
            if (string.IsNullOrEmpty(callerId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(id, callerId, userId, version, ct);
            return result switch
            {
                GetLeagueMemberGameweeksResult.NotFound         => Results.NotFound(new { error = "not_found" }),
                GetLeagueMemberGameweeksResult.ConfigMissing    => Results.BadRequest(new { error = "gameweek_config_missing" }),
                GetLeagueMemberGameweeksResult.CalendarNotFound => Results.NotFound(new { error = "tournament_not_found" }),
                GetLeagueMemberGameweeksResult.Found f          => Results.Ok(MemberGameweeksBody(f.View)),
                _                                               => Results.Problem()
            };
        });
    }

    // The /api/users/me/gameweeks shape, plus each round's frozen lineup and whether it is settled.
    // An unsettled round has null points, transferHit and grossPoints, and an empty breakdown.
    private static object MemberGameweeksBody(MemberGameweeks view) => new
    {
        runningTotal = view.RunningTotal,
        gameweeks = view.Gameweeks.Select(g => new
        {
            number = g.Number,
            roundLabel = g.RoundLabel,
            deadline = g.Deadline,
            settled = g.Score is not null,
            points = g.Score?.Points,
            transferHit = g.Score?.TransferHit,
            grossPoints = g.Score is null ? (double?)null : g.Score.Points + g.Score.TransferHit,
            captainPlayerId = g.Score?.CaptainPlayerId,
            lineup = (g.Lineup?.Slots ?? Array.Empty<LineupSlot>()).Select(s =>
            {
                view.Players.TryGetValue(s.PlayerId, out var player);
                return new
                {
                    s.PlayerId,
                    name = player?.Name,
                    position = player?.Position,
                    role = s.Role.ToString(),
                    s.BenchOrder
                };
            }),
            breakdown = g.Score is null ? Enumerable.Empty<object>() : GameweekEndpoints.Breakdown(g.Score, view.Players)
        })
    };
}
