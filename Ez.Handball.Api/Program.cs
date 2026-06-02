using Ez.Handball.Api.Middleware;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Infrastructure;
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

var storageConnection = builder.Configuration["Storage:ConnectionString"]
    ?? "UseDevelopmentStorage=true";

builder.Services.AddTableStorageInfrastructure(storageConnection);

builder.Services.AddAuthInfrastructure(builder.Configuration);

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep "sub" as "sub" for ClaimsPrincipal.UserId()
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

var permitLimit = builder.Configuration.GetValue("Auth:RateLimit:PermitLimit", 20);
var windowSeconds = builder.Configuration.GetValue("Auth:RateLimit:WindowSeconds", 60);
var sensitiveLimit = builder.Configuration.GetValue("Auth:RateLimit:SensitivePermitLimit", 5);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per-IP fixed windows. Per-email partitioning (spec) is deferred: the limiter runs
    // before model binding, so the request body isn't available here yet.
    options.AddPolicy("auth-global", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds)
            }));

    options.AddPolicy("auth-sensitive", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = sensitiveLimit,
                Window = TimeSpan.FromSeconds(windowSeconds)
            }));
});

builder.Services.AddScoped<IGetPlayerProfileUseCase, GetPlayerProfileUseCase>();
builder.Services.AddScoped<IGetPlayerStatsUseCase,   GetPlayerStatsUseCase>();
builder.Services.AddScoped<IGetPlayerHistoryUseCase, GetPlayerHistoryUseCase>();
builder.Services.AddScoped<IGetLeaderboardUseCase, GetLeaderboardUseCase>();
builder.Services.AddScoped<IGetMatchUseCase, GetMatchUseCase>();
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

app.MapGet("/api/players/{playerId}/stats", async (
    string playerId,
    IGetPlayerStatsUseCase uc,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    var result = await uc.ExecuteAsync(playerId, ct);
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

app.MapGet("/api/leaderboard", async Task<IResult> (
    string? metric,
    string? season,
    string? tournamentId,
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

    var off = offset ?? 0;
    var lim = limit ?? 50;
    if (off < 0 || lim < 1 || lim > 200)
        return Results.BadRequest(new { error = "invalid_pagination" });

    var query = new LeaderboardQuery(parsedMetric, season, tournamentId, parsedGender);
    var result = await uc.ExecuteAsync(query, off, lim, ct);
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

static bool TryNormalizeGender(string? value, out string? gender)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        gender = null;
        return true;
    }
    switch (value.ToLowerInvariant())
    {
        case "karlar": gender = "karlar"; return true;
        case "kvenna": gender = "kvenna"; return true;
        default: gender = null; return false;
    }
}

public partial class Program { }  // for WebApplicationFactory<Program>
