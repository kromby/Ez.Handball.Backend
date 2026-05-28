using Azure.Data.Tables;
using Ez.Handball.Api.Middleware;
using Ez.Handball.Api.Services;
using Ez.Handball.Infrastructure.TableAccess;

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
            // No origins configured → effectively same-origin only.
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

builder.Services.AddSingleton(_ => new TableServiceClient(storageConnection));
builder.Services.AddSingleton<ITableQuery, TableQuery>();
builder.Services.AddSingleton<Func<DateOnly>>(_ => () => DateOnly.FromDateTime(DateTime.UtcNow));
builder.Services.AddSingleton<IPlayerLookupService, PlayerLookupService>();
builder.Services.AddSingleton<IPlayerStatsService, PlayerStatsService>();

var app = builder.Build();

app.UseMiddleware<ErrorJsonMiddleware>();
app.UseHttpLogging();
app.UseCors(CorsPolicy);

app.MapGet("/api/players/{playerId}", async (
    string playerId,
    IPlayerLookupService lookup,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    var profile = await lookup.GetPlayerAsync(playerId, ct);
    return profile is null
        ? Results.NotFound(new { error = "player_not_found" })
        : Results.Ok(profile);
});

app.MapGet("/api/players/{playerId}/stats", async (
    string playerId,
    IPlayerLookupService lookup,
    IPlayerStatsService stats,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(playerId))
        return Results.BadRequest(new { error = "invalid_player_id" });

    var profile = await lookup.GetPlayerAsync(playerId, ct);
    if (profile is null)
        return Results.NotFound(new { error = "player_not_found" });

    var rows = await stats.GetStatsAsync(playerId, ct);
    return Results.Ok(new { playerId, stats = rows });
});

app.Run();

public partial class Program { }  // for WebApplicationFactory<Program>
