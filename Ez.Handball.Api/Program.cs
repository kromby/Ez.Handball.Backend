using Ez.Handball.Api.Middleware;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Infrastructure;

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

builder.Services.AddScoped<IGetPlayerProfileUseCase, GetPlayerProfileUseCase>();
builder.Services.AddScoped<IGetPlayerStatsUseCase,   GetPlayerStatsUseCase>();
builder.Services.AddScoped<IGetPlayerHistoryUseCase, GetPlayerHistoryUseCase>();
builder.Services.AddScoped<IGetMatchUseCase, GetMatchUseCase>();

var app = builder.Build();

app.UseMiddleware<ErrorJsonMiddleware>();
app.UseHttpLogging();
app.UseCors(CorsPolicy);

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

public partial class Program { }  // for WebApplicationFactory<Program>
