using Ez.Handball.Api.Auth;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;

namespace Ez.Handball.Api;

public sealed record LineupStarterDto(string? PlayerId, string? Role);

public sealed record SetLineupRequest(
    string? Flavor,
    string? Season,
    string? TournamentId,
    int? RuleSetVersion,
    IReadOnlyList<LineupStarterDto>? Starters,
    IReadOnlyList<string>? Bench);

public static class LineupEndpoints
{
    private const string Base = "/api/users/me/lineup";

    public static void MapLineupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Base).RequireAuthorization();

        group.MapGet("", async (
            string? flavor, string? season, string? tournamentId, int? ruleSetVersion,
            HttpContext http, IGetLineupUseCase uc, CancellationToken ct) =>
        {
            if (!IsFantasy(flavor)) return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var result = await uc.ExecuteAsync(userId, season, tournamentId, ruleSetVersion, ct);
            return result switch
            {
                GetLineupResult.RuleSetNotFound  => Results.BadRequest(new { error = "invalid_rule_set" }),
                GetLineupResult.NotSet n         => Results.Ok(EmptyBody(n.CaptainMultiplier)),
                GetLineupResult.Found f          => Results.Ok(LineupBody(f.View)),
                _                                => Results.Problem()
            };
        });

        group.MapPut("", async (
            SetLineupRequest req, HttpContext http, ISetLineupUseCase uc, IGetCurrentGameweekUseCase gw, CancellationToken ct) =>
        {
            if (!IsFantasy(req.Flavor)) return Results.BadRequest(new { error = "invalid_flavor" });

            var userId = http.User.UserId();
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            if (!TryBuildLineup(req, out var lineup))
                return Results.BadRequest(new { error = "malformed_body" });

            var result = await uc.ExecuteAsync(userId, lineup, req.Season, req.TournamentId, req.RuleSetVersion, ct);
            if (result is SetLineupResult.Committed c)
            {
                var gameweek = await GameweekEcho.BuildAsync(gw, ct);
                return Results.Ok(new { lineup = LineupBody(c.View), gameweek });
            }
            return result switch
            {
                SetLineupResult.NoTeam          => Results.Json(new { error = "no_team" }, statusCode: StatusCodes.Status409Conflict),
                SetLineupResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
                SetLineupResult.Rejected r      => Results.Json(new { violations = r.Violations.Select(v => new { code = v.Code, message = v.Message }) }, statusCode: StatusCodes.Status422UnprocessableEntity),
                _                               => Results.Problem()
            };
        });
    }

    // Build the domain Lineup from the request. Returns false on a structurally malformed body
    // (missing arrays, blank ids, or a starter role that isn't Starter/Captain/Vice). Business
    // rules (counts, positions, captaincy) are the validator's job, not the parser's.
    private static bool TryBuildLineup(SetLineupRequest req, out Lineup lineup)
    {
        lineup = new Lineup(Array.Empty<LineupSlot>());
        if (req.Starters is null || req.Bench is null) return false;

        var slots = new List<LineupSlot>(req.Starters.Count + req.Bench.Count);

        foreach (var s in req.Starters)
        {
            if (string.IsNullOrWhiteSpace(s.PlayerId)) return false;
            if (!Enum.TryParse<LineupRole>(s.Role, ignoreCase: true, out var role)) return false;
            if (role == LineupRole.Bench) return false; // bench players belong in Bench[]
            slots.Add(new LineupSlot(s.PlayerId, role, null));
        }

        for (var i = 0; i < req.Bench.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(req.Bench[i])) return false;
            slots.Add(new LineupSlot(req.Bench[i], LineupRole.Bench, i));
        }

        lineup = new Lineup(slots);
        return true;
    }

    private static bool IsFantasy(string? flavor)
        => string.IsNullOrWhiteSpace(flavor) || flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase);

    private static object EmptyBody(double captainMultiplier) => new
    {
        flavor = "fantasy",
        starters = Array.Empty<object>(),
        bench = Array.Empty<object>(),
        captainId = (string?)null,
        viceId = (string?)null,
        isValid = false,
        violations = Array.Empty<object>(),
        captainMultiplier
    };

    private static object LineupBody(LineupView view) => new
    {
        flavor = "fantasy",
        starters = view.Slots
            .Where(s => s.Role != LineupRole.Bench)
            .Select(s => new
            {
                playerId = s.PlayerId, name = s.Name, clubName = s.ClubName,
                position = s.Position, price = s.Price, role = s.Role.ToString()
            }),
        bench = view.Slots
            .Where(s => s.Role == LineupRole.Bench)
            .OrderBy(s => s.BenchOrder)
            .Select(s => new
            {
                playerId = s.PlayerId, name = s.Name, clubName = s.ClubName,
                position = s.Position, price = s.Price, benchOrder = s.BenchOrder
            }),
        captainId = view.Slots.FirstOrDefault(s => s.Role == LineupRole.Captain)?.PlayerId,
        viceId = view.Slots.FirstOrDefault(s => s.Role == LineupRole.Vice)?.PlayerId,
        isValid = view.IsValid,
        violations = view.Violations.Select(v => new { code = v.Code, message = v.Message }),
        captainMultiplier = view.CaptainMultiplier
    };
}
