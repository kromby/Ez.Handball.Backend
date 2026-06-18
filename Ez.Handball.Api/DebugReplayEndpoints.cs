using System.Security.Cryptography;
using System.Text;
using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

// Debug-only replay harness controls (#96). Mapped only when Debug:GameClock:OverrideEnabled is on
// (so absent in production) and gated behind an X-Debug-Key shared secret. Domain-clock time only.
public static class DebugReplayEndpoints
{
    public const string HeaderName = "X-Debug-Key";

    public sealed record ClockRequest(string Mode, DateTimeOffset? Date, int? Version);

    public static void MapDebugReplayEndpoints(this WebApplication app, string? adminKey)
    {
        var group = app.MapGroup("/api/debug").AddEndpointFilter(new DebugKeyFilter(adminKey));

        group.MapPost("/clock", async (ClockRequest body, IAdvanceClockUseCase uc, CancellationToken ct) =>
        {
            var mode = ParseMode(body.Mode);
            if (mode is null) return Results.BadRequest(new { error = "invalid_mode" });
            if (mode == ClockMode.Set && body.Date is null) return Results.BadRequest(new { error = "date_required" });

            return MapClock(await uc.ExecuteAsync(mode.Value, body.Date, body.Version, ct));
        });

        group.MapPost("/settle-round", async (string round, int? version,
            ISettleRoundForAllTeamsUseCase uc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(round)) return Results.BadRequest(new { error = "invalid_round" });
            return MapSettle(await uc.ExecuteAsync(round, version, ct));
        });

        group.MapPost("/advance-and-settle", async (int? version,
            IAdvanceClockUseCase clock, ISettleRoundForAllTeamsUseCase settle, CancellationToken ct) =>
        {
            var advanced = await clock.ExecuteAsync(ClockMode.AdvanceRound, null, version, ct);
            if (advanced is not AdvanceClockResult.Moved moved)
                return MapClock(advanced); // Disabled / ConfigMissing / CalendarUnavailable / NothingToAdvance

            var settleResult = await settle.ExecuteAsync(moved.RoundLabel!, version, ct);
            if (settleResult is not SettleRoundForAllTeamsResult.Completed completed)
                return MapSettle(settleResult);

            return Results.Ok(new { virtualNow = moved.VirtualNow, round = moved.RoundLabel, report = completed.Report });
        });
    }

    private static ClockMode? ParseMode(string mode) => mode switch
    {
        "set" => ClockMode.Set,
        "advance-deadline" => ClockMode.AdvanceDeadline,
        "advance-round" => ClockMode.AdvanceRound,
        "clear" => ClockMode.Clear,
        _ => null
    };

    private static IResult MapClock(AdvanceClockResult result) => result switch
    {
        AdvanceClockResult.Disabled => Results.Json(new { error = "override_disabled" }, statusCode: StatusCodes.Status409Conflict),
        AdvanceClockResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
        AdvanceClockResult.CalendarUnavailable => Results.NotFound(new { error = "tournament_not_found" }),
        AdvanceClockResult.NothingToAdvance => Results.Json(new { error = "nothing_to_advance" }, statusCode: StatusCodes.Status409Conflict),
        AdvanceClockResult.Cleared => Results.Ok(new { virtualNow = (DateTimeOffset?)null, round = (string?)null, enabled = true }),
        AdvanceClockResult.Moved m => Results.Ok(new { virtualNow = m.VirtualNow, round = m.RoundLabel, enabled = true }),
        _ => Results.Problem()
    };

    private static IResult MapSettle(SettleRoundForAllTeamsResult result) => result switch
    {
        SettleRoundForAllTeamsResult.ConfigMissing => Results.BadRequest(new { error = "gameweek_config_missing" }),
        SettleRoundForAllTeamsResult.RoundNotFound => Results.NotFound(new { error = "round_not_found" }),
        SettleRoundForAllTeamsResult.RuleSetMissing => Results.BadRequest(new { error = "rule_set_missing" }),
        SettleRoundForAllTeamsResult.Completed c => Results.Ok(c.Report),
        _ => Results.Problem()
    };
}

internal sealed class DebugKeyFilter : IEndpointFilter
{
    private readonly string? _adminKey;

    public DebugKeyFilter(string? adminKey) => _adminKey = adminKey;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        // Secure default: if the flag enabled the endpoints but no key is configured, refuse — never an open door.
        if (string.IsNullOrEmpty(_adminKey))
            return Results.Json(new { error = "debug_key_not_configured" }, statusCode: StatusCodes.Status403Forbidden);

        var provided = ctx.HttpContext.Request.Headers[DebugReplayEndpoints.HeaderName].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(_adminKey)))
            return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

        return await next(ctx);
    }
}
