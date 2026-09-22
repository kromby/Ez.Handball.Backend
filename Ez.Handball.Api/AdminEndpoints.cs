using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

public record SetPlayerPositionRequest(string Position, string? PositionSecondary);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("AdminOnly");

        admin.MapGet("/tournaments", async (
            IGetTournamentStatusUseCase uc, CancellationToken ct) =>
        {
            var tournaments = await uc.ExecuteAsync(ct);
            return Results.Ok(tournaments.Select(t => new
            {
                tournamentId = t.TournamentId,
                name = t.Name,
                gender = t.Gender,
                type = t.Type,
                competitionId = t.CompetitionId,
                competitionName = t.CompetitionName,
                season = t.Season,
                active = t.Active,
                ingest = t.Ingest,
                ingestHbStatz = t.IngestHbStatz,
                priority = t.Priority
            }));
        });

        admin.MapGet("/games", async (
            string? season, IGetAdminGameStatusUseCase uc, CancellationToken ct) =>
        {
            var tournaments = await uc.ExecuteAsync(season, ct);
            return Results.Ok(tournaments.Select(t => new
            {
                tournamentId = t.TournamentId,
                name = t.Name,
                competitionName = t.CompetitionName,
                lastSyncedAt = t.LastSyncedAt,
                rounds = t.Rounds.Select(r => new
                {
                    round = r.Round,
                    games = r.Games.Select(g => new
                    {
                        matchId = g.MatchId,
                        date = g.Date,
                        venue = g.Venue,
                        homeTeamName = g.HomeTeamName,
                        awayTeamName = g.AwayTeamName,
                        status = g.Status,
                        ingested = g.Ingested,
                        hbStatzIngested = g.HbStatzIngested
                    })
                })
            }));
        });

        admin.MapPost("/sync", async (
            ITriggerIngestionSyncUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(ct);
            return result.Success
                ? Results.Ok(new { synced = result.Synced, failed = result.Failed })
                : Results.Json(new { error = result.Error ?? "sync_failed" }, statusCode: StatusCodes.Status502BadGateway);
        });

        admin.MapPost("/hbstatz-sync", async (
            string? tournamentId, string? round, string? matchId,
            ITriggerHbStatzSyncUseCase uc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(tournamentId) && (!string.IsNullOrWhiteSpace(round) || !string.IsNullOrWhiteSpace(matchId)))
                return Results.BadRequest(new { error = "tournamentId_required_for_scoped_sync" });

            var result = await uc.ExecuteAsync(tournamentId, round, matchId, ct);
            return result.Success
                ? Results.Ok(new
                {
                    matchesChecked = result.MatchesChecked,
                    matchesSynced = result.MatchesSynced,
                    unmatched = result.Unmatched,
                    failed = result.Failed
                })
                : Results.Json(new { error = result.Error ?? "sync_failed" }, statusCode: StatusCodes.Status502BadGateway);
        });

        admin.MapGet("/players/missing-position", async (
            string? clubId, string? gender,
            IGetPlayersMissingPositionUseCase uc, CancellationToken ct) =>
        {
            var players = await uc.ExecuteAsync(clubId, gender, ct);
            return Results.Ok(players.Select(p => new
            {
                playerId = p.PlayerId,
                name = p.Name,
                clubId = p.ClubId,
                clubName = p.ClubName,
                gender = p.Gender,
                position = p.Position
            }));
        });

        admin.MapPost("/mini-leagues/backfill-membership-index", async (
            bool? dryRun, IBackfillMiniLeagueMembershipIndexUseCase uc, CancellationToken ct) =>
        {
            var runDry = dryRun ?? true;
            var result = await uc.ExecuteAsync(runDry, ct);
            return Results.Ok(new
            {
                membersScanned = result.MembersScanned,
                written = result.Written,
                dryRun = runDry
            });
        });

        // Production settlement (#136): settles every team for one round, or — with no round — for every
        // complete gameweek. Idempotent, so it doubles as the backfill for rounds that were never settled.
        admin.MapPost("/gameweeks/settle", async (
            string? round, int? version,
            ISettleRoundForAllTeamsUseCase settleRound, ISettleCompletedRoundsUseCase settleCompleted,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(round))
                return DebugReplayEndpoints.MapSettle(await settleRound.ExecuteAsync(round, version, ct));

            return await settleCompleted.ExecuteAsync(null, version, ct) switch
            {
                SettleCompletedRoundsResult.ConfigMissing       => Results.BadRequest(new { error = "gameweek_config_missing" }),
                SettleCompletedRoundsResult.CalendarUnavailable => Results.NotFound(new { error = "tournament_not_found" }),
                SettleCompletedRoundsResult.RoundFailed f       => DebugReplayEndpoints.MapSettle(f.Reason),
                SettleCompletedRoundsResult.Completed c         => Results.Ok(new { rounds = c.Rounds }),
                _                                               => Results.Problem()
            };
        });

        admin.MapPost("/players/{playerId}/position", async (
            string playerId, SetPlayerPositionRequest body,
            ISetPlayerPositionUseCase uc, CancellationToken ct) =>
        {
            var result = await uc.ExecuteAsync(playerId, body.Position, body.PositionSecondary, ct);
            return result switch
            {
                SetPlayerPositionResult.Ok             => Results.Ok(),
                SetPlayerPositionResult.InvalidPosition => Results.BadRequest(new { error = "invalid_position" }),
                SetPlayerPositionResult.PlayerNotFound  => Results.NotFound(new { error = "player_not_found" }),
                _                                       => Results.Problem()
            };
        });
    }
}
