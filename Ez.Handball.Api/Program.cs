using Ez.Handball.Api;
using Ez.Handball.Api.Auth;
using Ez.Handball.Api.Middleware;
using Ez.Handball.Api.Serialization;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.BuyFunctions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.Security;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "WebUi";

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            return;
        }
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpLogging(_ => { });

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new TournamentTypeJsonConverter()));

var storageConnection = builder.Configuration["Storage:ConnectionString"]
    ?? "UseDevelopmentStorage=true";

builder.Services.AddTableStorageInfrastructure(storageConnection);

builder.Services.AddAuthInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
// Configure JwtBearer from the DI-registered JwtSettings (the same instance the token
// issuer uses) so the validating key can never diverge from the signing key. Reading the
// key eagerly from builder.Configuration here would capture a stale value if a later config
// source (e.g. WebApplicationFactory's in-memory overrides) is layered after this point,
// producing IDX10511 signature-validation failures against freshly issued tokens.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtSettings>((options, jwt) =>
    {
        options.MapInboundClaims = false; // keep "sub" as "sub" for ClaimsPrincipal.UserId()
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.SigningKey)) { KeyId = "ezhb-hs256" }, // must match JwtTokenService.SigningKeyId
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per-IP fixed windows. Per-email partitioning (spec) is deferred: the limiter runs
    // before model binding, so the request body isn't available here yet.
    // Limits are resolved at request time from DI config so later config sources
    // (e.g. WebApplicationFactory in-memory overrides) take effect.
    options.AddPolicy("auth-global", httpContext =>
    {
        var cfg = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permit = cfg.GetValue("Auth:RateLimit:PermitLimit", 20);
        var window = cfg.GetValue("Auth:RateLimit:WindowSeconds", 60);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromSeconds(window)
            });
    });

    options.AddPolicy("auth-sensitive", httpContext =>
    {
        var cfg = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permit = cfg.GetValue("Auth:RateLimit:SensitivePermitLimit", 5);
        var window = cfg.GetValue("Auth:RateLimit:WindowSeconds", 60);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromSeconds(window)
            });
    });
});

builder.Services.AddScoped<IGetPlayerProfileUseCase, GetPlayerProfileUseCase>();
builder.Services.AddScoped<IGetPlayerStatsUseCase,   GetPlayerStatsUseCase>();
builder.Services.AddScoped<IGetPlayerHistoryUseCase, GetPlayerHistoryUseCase>();
builder.Services.AddScoped<IGetLeaderboardUseCase, GetLeaderboardUseCase>();
builder.Services.AddScoped<IGetMatchUseCase, GetMatchUseCase>();
builder.Services.AddScoped<IGetClubsUseCase, GetClubsUseCase>();
builder.Services.AddScoped<IGetSeasonsUseCase, GetSeasonsUseCase>();
builder.Services.AddScoped<IGetTournamentsUseCase, GetTournamentsUseCase>();
builder.Services.AddScoped<ITournamentScopeResolver, TournamentScopeResolver>();
builder.Services.AddScoped<IPlayerStatsAggregator, PlayerStatsAggregator>();
builder.Services.AddScoped<IGetPlayerRatingUseCase, GetPlayerRatingUseCase>();
builder.Services.AddScoped<FantasyPlayerRatingFunction>();
builder.Services.AddScoped<FantasyPricing>();
builder.Services.AddScoped<IPlayerSalaryService, PlayerSalaryService>();
builder.Services.AddScoped<IGetPlayerSalaryUseCase, GetPlayerSalaryUseCase>();
builder.Services.AddScoped<IBuyPlayerFunction, FantasyBuyPlayerFunction>();
builder.Services.AddScoped<IBuyPlayerFunction, ManagerBuyPlayerFunction>();
builder.Services.AddScoped<IGetBuyDecisionUseCase, GetBuyDecisionUseCase>();
builder.Services.AddScoped<IPlayerRatingFunction, FantasyPlayerRatingFunction>();
builder.Services.AddScoped<IPlayerRatingFunction, ManagerPlayerRatingFunction>();
builder.Services.AddSingleton(new ShortlistSettings(
    builder.Configuration.GetValue("Shortlist:MaxSize", 20)));
builder.Services.AddScoped<IAddToShortlistUseCase, AddToShortlistUseCase>();
builder.Services.AddScoped<IRemoveFromShortlistUseCase, RemoveFromShortlistUseCase>();
builder.Services.AddScoped<IGetShortlistUseCase, GetShortlistUseCase>();
builder.Services.AddScoped<IGetSquadUseCase, GetSquadUseCase>();
builder.Services.AddScoped<IBuyPlayerUseCase, BuyPlayerUseCase>();
builder.Services.AddScoped<ISellPlayerUseCase, SellPlayerUseCase>();
builder.Services.AddScoped<ITeamProvisioningService, TeamProvisioningService>();
builder.Services.AddScoped<IGetSquadConstraintsUseCase, GetSquadConstraintsUseCase>();
builder.Services.AddScoped<IRegisterUseCase, RegisterUseCase>();
builder.Services.AddScoped<ILoginUseCase, LoginUseCase>();
builder.Services.AddScoped<IRefreshTokenUseCase, RefreshTokenUseCase>();
builder.Services.AddScoped<ILogoutUseCase, LogoutUseCase>();
builder.Services.AddScoped<IVerifyEmailUseCase, VerifyEmailUseCase>();
builder.Services.AddScoped<IRequestPasswordResetUseCase, RequestPasswordResetUseCase>();
builder.Services.AddScoped<IResetPasswordUseCase, ResetPasswordUseCase>();
builder.Services.AddScoped<IUpdateProfileUseCase, UpdateProfileUseCase>();
builder.Services.AddScoped<IResendVerificationUseCase, ResendVerificationUseCase>();

var app = builder.Build();

app.UseMiddleware<ErrorJsonMiddleware>();
app.UseHttpLogging();
app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/players/{playerId}", async (
    string playerId,
    IGetPlayerProfileUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    var result = await uc.ExecuteAsync(playerId, ct);
    return result switch
    {
        GetPlayerProfileResult.NotFound       => Results.NotFound(new { error = "player_not_found" }),
        GetPlayerProfileResult.Found f        => Results.Ok(f.Player),
        _                                     => Results.Problem()
    };
});

app.MapGet("/api/players/{playerId}/stats", async Task<IResult> (
    string playerId,
    string? season,
    string? tournamentId,
    string? competitionId,
    string? type,
    IGetPlayerStatsUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    if (!TryParseTournamentType(type, out var parsedType))
        return Results.BadRequest(new { error = "invalid_type" });

    if (!string.IsNullOrWhiteSpace(tournamentId) && !string.IsNullOrWhiteSpace(competitionId))
        return Results.BadRequest(new { error = "invalid_scope" });

    var query = new PlayerStatsQuery(season, tournamentId, competitionId, parsedType);
    var result = await uc.ExecuteAsync(playerId, query, ct);
    return result switch
    {
        GetPlayerStatsResult.NotFound         => Results.NotFound(new { error = "player_not_found" }),
        GetPlayerStatsResult.Found f          => Results.Ok(new { playerId = f.PlayerId, stats = f.Stats }),
        _                                     => Results.Problem()
    };
});

app.MapGet("/api/players/{playerId}/history", async (
    string playerId,
    IGetPlayerHistoryUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    var result = await uc.ExecuteAsync(playerId, ct);
    return result switch
    {
        GetPlayerHistoryResult.NotFound       => Results.NotFound(new { error = "player_not_found" }),
        GetPlayerHistoryResult.Found f        => Results.Ok(new
        {
            playerId = f.PlayerId,
            history  = f.History.Entries,
            totals   = f.History.Totals
        }),
        _                                     => Results.Problem()
    };
});

app.MapGet("/api/players/{playerId}/rating", async Task<IResult> (
    string playerId,
    string? flavor,
    string? season,
    string? tournamentId,
    string? competitionId,
    int? ruleSetVersion,
    string? type,
    DateOnly? asOf,
    IGetPlayerRatingUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    if (!TryParseFlavor(flavor, out var parsedFlavor))
        return Results.BadRequest(new { error = "invalid_flavor" });

    if (!TryParseTournamentType(type, out var parsedType))
        return Results.BadRequest(new { error = "invalid_type" });

    if (!string.IsNullOrWhiteSpace(tournamentId) && !string.IsNullOrWhiteSpace(competitionId))
        return Results.BadRequest(new { error = "invalid_scope" });

    var context = new PlayerRatingContext(season, tournamentId, competitionId, ruleSetVersion, parsedType, asOf);
    var result = await uc.ExecuteAsync(playerId, parsedFlavor, context, ct);
    return result switch
    {
        GetPlayerRatingResult.NotFound         => Results.NotFound(new { error = "player_not_found" }),
        GetPlayerRatingResult.InvalidFlavor    => Results.BadRequest(new { error = "invalid_flavor" }),
        GetPlayerRatingResult.RuleSetNotFound  => Results.BadRequest(new { error = "invalid_rule_set" }),
        GetPlayerRatingResult.Found f          => Results.Ok(f.Rating),
        _                                     => Results.Problem()
    };
});

app.MapGet("/api/players/{playerId}/salary", async Task<IResult> (
    string playerId,
    string? season,
    string? tournamentId,
    int? ruleSetVersion,
    IGetPlayerSalaryUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    var result = await uc.ExecuteAsync(playerId, ruleSetVersion, season, tournamentId, ct);
    return result switch
    {
        GetPlayerSalaryResult.NotFound        => Results.NotFound(new { error = "player_not_found" }),
        GetPlayerSalaryResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
        GetPlayerSalaryResult.Found f         => Results.Ok(f.Salary),
        _                                     => Results.Problem()
    };
});

app.MapGet("/api/players/{playerId}/buy", async Task<IResult> (
    string playerId,
    string? flavor,
    string? season,
    string? tournamentId,
    int? ruleSetVersion,
    HttpContext http,
    IGetBuyDecisionUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    if (!TryParseFlavor(flavor, out var parsedFlavor))
        return Results.BadRequest(new { error = "invalid_flavor" });

    var userId = http.User.UserId();
    if (string.IsNullOrEmpty(userId))
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

    var context = new BuyPlayerContext(season, tournamentId, ruleSetVersion);
    var result = await uc.ExecuteAsync(userId, playerId, parsedFlavor, context, ct);
    return result switch
    {
        BuyDecisionResult.PlayerNotFound  => Results.NotFound(new { error = "player_not_found" }),
        BuyDecisionResult.InvalidFlavor   => Results.BadRequest(new { error = "invalid_flavor" }),
        BuyDecisionResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
        BuyDecisionResult.Decided d       => Results.Ok(d.Decision),
        _                                 => Results.Problem()
    };
}).RequireAuthorization();

app.MapGet("/api/leaderboard", async Task<IResult> (
    string? metric,
    string? season,
    string? tournamentId,
    string? competitionId,
    string? type,
    string? gender,
    int? offset,
    int? limit,
    IGetLeaderboardUseCase uc,
    CancellationToken ct) =>
{
    if (!TryParseMetric(metric, out var parsedMetric))
        return Results.BadRequest(new { error = "invalid_metric" });

    if (!TryNormalizeGender(gender, out var parsedGender))
        return Results.BadRequest(new { error = "invalid_gender" });

    if (!TryParseTournamentType(type, out var parsedType))
        return Results.BadRequest(new { error = "invalid_type" });

    if (!string.IsNullOrWhiteSpace(tournamentId) && !string.IsNullOrWhiteSpace(competitionId))
        return Results.BadRequest(new { error = "invalid_scope" });

    var off = offset ?? 0;
    var lim = limit ?? 50;
    if (off < 0 || lim < 1 || lim > 200)
        return Results.BadRequest(new { error = "invalid_pagination" });

    var request = new LeaderboardRequest(
        parsedMetric, season, tournamentId, competitionId, parsedType, parsedGender);
    var result = await uc.ExecuteAsync(request, off, lim, ct);
    return Results.Ok(result);
});

app.MapGet("/api/matches/{matchId}", async (
    string matchId,
    IGetMatchUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(matchId))
        return Results.BadRequest(new { error = "invalid_match_id" });

    var result = await uc.ExecuteAsync(matchId, ct);
    return result switch
    {
        GetMatchResult.NotFound       => Results.NotFound(new { error = "match_not_found" }),
        GetMatchResult.Found f        => Results.Ok(f.Match),
        _                             => Results.Problem()
    };
});

app.MapGet("/api/clubs", async (
    IGetClubsUseCase uc,
    CancellationToken ct) =>
{
    var clubs = await uc.ExecuteAsync(ct);
    return Results.Ok(clubs);
});

app.MapGet("/api/seasons", async (
    IGetSeasonsUseCase uc,
    CancellationToken ct) =>
{
    var seasons = await uc.ExecuteAsync(ct);
    return Results.Ok(seasons);
});

app.MapGet("/api/tournaments", async (
    string? season,
    IGetTournamentsUseCase uc,
    CancellationToken ct) =>
{
    var tournaments = await uc.ExecuteAsync(season, ct);
    return Results.Ok(tournaments);
});

app.MapGet("/api/squad/constraints", async Task<IResult> (
    string? flavor,
    int? ruleSetVersion,
    IGetSquadConstraintsUseCase uc,
    CancellationToken ct) =>
{
    // Fantasy-only: blank or "fantasy" is accepted; anything else is rejected.
    // Mirrors the edge check in SquadEndpoints — flavor never reaches the use case.
    if (!string.IsNullOrWhiteSpace(flavor)
        && !flavor.Equals("fantasy", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "invalid_flavor" });

    var result = await uc.ExecuteAsync(ruleSetVersion, ct);
    return result switch
    {
        GetSquadConstraintsResult.RuleSetNotFound => Results.BadRequest(new { error = "invalid_rule_set" }),
        GetSquadConstraintsResult.Found f => Results.Ok(new
        {
            ruleSetVersion = f.Constraints.Version,
            maxSquadSize   = f.Constraints.MaxSquadSize,
            startingCap    = new PlayerCost(f.Constraints.StartingCap, f.Constraints.Currency),
            posLimits      = f.Constraints.PositionLimits
        }),
        _ => Results.Problem()
    };
});

app.MapGet("/api/genders", () => Results.Ok(Genders.All));

app.MapAuthEndpoints();

app.MapShortlistEndpoints();
app.MapSquadEndpoints();

app.Run();

static bool TryParseMetric(string? value, out LeaderboardMetric metric)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        metric = LeaderboardMetric.Goals;
        return true;
    }
    return Enum.TryParse(value, ignoreCase: true, out metric) && Enum.IsDefined(metric);
}

static bool TryParseFlavor(string? value, out GameFlavor flavor)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        flavor = GameFlavor.Fantasy;
        return true;
    }
    return Enum.TryParse(value, ignoreCase: true, out flavor) && Enum.IsDefined(flavor);
}

static bool TryParseTournamentType(string? value, out TournamentType? type)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        type = null;
        return true;
    }
    if (TournamentTypes.TryParse(value, out var parsed))
    {
        type = parsed;
        return true;
    }
    type = null;
    return false;
}

static bool TryNormalizeGender(string? value, out string? gender)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        gender = null;
        return true;
    }

    var match = Genders.All.FirstOrDefault(
        g => string.Equals(g.Value, value, StringComparison.OrdinalIgnoreCase));
    gender = match?.Value;
    return match is not null;
}

public partial class Program { }  // for WebApplicationFactory<Program>
