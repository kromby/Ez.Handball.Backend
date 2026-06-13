using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public static class GameweekEndpoints
{
    public static void MapGameweekEndpoints(this WebApplication app)
    {
        app.MapGet("/api/gameweeks", async (
            int? version, IGetGameweeksUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(version, ct);
            return result switch
            {
                GetGameweeksResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
                GetGameweeksResult.NotFound      => Results.NotFound(new { error = "tournament_not_found" }),
                GetGameweeksResult.Found f       => Results.Ok(f.Gameweeks.Select(Body)),
                _                                => Results.Problem()
            };
        });

        app.MapGet("/api/gameweeks/current", async (
            int? version, IGetCurrentGameweekUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(version, ct);
            return result switch
            {
                GetCurrentGameweekResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
                GetCurrentGameweekResult.NotFound      => Results.NotFound(new { error = "tournament_not_found" }),
                GetCurrentGameweekResult.Found f       => Results.Ok(new
                {
                    current = f.Current is null ? null : Body(f.Current),
                    lastSettled = f.LastSettled is null ? null : Body(f.LastSettled)
                }),
                _ => Results.Problem()
            };
        });

        app.MapGet("/api/users/me/gameweeks", async (
            HttpContext http, IGetMyGameweekScoresUseCase uc, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, ct);
            return Results.Ok(new
            {
                runningTotal = result.RunningTotal,
                gameweeks = result.Gameweeks.Select(g => new
                {
                    roundLabel = g.RoundLabel,
                    points = g.Points,
                    captainPlayerId = g.CaptainPlayerId,
                    breakdown = g.Breakdown
                })
            });
        }).RequireAuthorization();

        app.MapPost("/api/gameweeks/settle", async (
            string round, string? teamId, HttpContext http, int? version,
            ISettleGameweekUseCase uc, CancellationToken ct) =>
        {
            // Authed: the caller settles their own team unless an explicit teamId is given (admin/ingestion).
            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
            if (string.IsNullOrWhiteSpace(round))
                return Results.BadRequest(new { error = "invalid_round" });

            var team = string.IsNullOrWhiteSpace(teamId) ? GameTeamId.For(userId, GameFlavor.Fantasy) : teamId;
            var result = await uc.ExecuteAsync(userId, team, round, version, ct);
            return result switch
            {
                SettleGameweekResult.ConfigMissing      => Results.BadRequest(new { error = "gameweek_config_missing" }),
                SettleGameweekResult.NotFound           => Results.NotFound(new { error = "round_not_found" }),
                SettleGameweekResult.RuleSetMissing     => Results.BadRequest(new { error = "rule_set_missing" }),
                SettleGameweekResult.NoSnapshotPossible => Results.Json(new { error = "no_lineup" }, statusCode: StatusCodes.Status409Conflict),
                SettleGameweekResult.NotReady           => Results.Json(new { error = "not_ready" }, statusCode: StatusCodes.Status409Conflict),
                SettleGameweekResult.Settled s          => Results.Ok(new { round = s.Score.RoundLabel, points = s.Score.Points }),
                _                                       => Results.Problem()
            };
        }).RequireAuthorization();
    }

    internal static object Body(Gameweek g) => new
    {
        number = g.Number,
        roundLabel = g.RoundLabel,
        tournamentId = g.TournamentId,
        deadline = g.Deadline,
        status = g.Status.ToString(),
        matches = g.Matches.Select(m => new
        {
            matchId = m.MatchId,
            date = m.Date,
            isFinal = m.IsFinal,
            homeTeamId = m.HomeTeamId,
            awayTeamId = m.AwayTeamId
        })
    };
}
