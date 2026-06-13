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
