using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class PlayerEndpointsTests : IClassFixture<PlayerEndpointsTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        static Factory()
        {
            // Program reads Cors:AllowedOrigins eagerly while the host builder is
            // assembled, so the origin must come from a config source that is
            // present before the host builds. Environment variables are added by
            // WebApplication.CreateBuilder by default, so they win that race
            // (ConfigureAppConfiguration on the factory is applied too late).
            // "5500" is the Live Server port the static Web UI runs on.
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:5500");
            Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-test-signing-key-32-bytes-min!!");
        }

        public Mock<IGetPlayerProfileUseCase> Profile { get; } = new();
        public Mock<IGetPlayerStatsUseCase>   Stats   { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                Replace(services, Profile.Object);
                Replace(services, Stats.Object);
            });
            return base.CreateHost(builder);
        }

        private static void Replace<T>(IServiceCollection services, T instance) where T : class
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(T));
            services.Remove(descriptor);
            services.AddSingleton(instance);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlayerEndpointsTests(Factory factory)
    {
        _factory = factory;
        _factory.Profile.Reset();
        _factory.Stats.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetPlayer_NotFound_Returns404WithErrorJson()
    {
        _factory.Profile
            .Setup(s => s.ExecuteAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerProfileResult.NotFound());

        var response = await _client.GetAsync("/api/players/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetPlayer_Existing_Returns200WithProfile()
    {
        var player = new Player(
            "12345", "Aron Pálmarsson", "23",
            new DateOnly(1990, 7, 19),
            35, "385-karlar", "385", "Stjarnan", "karlar", "VS", false, "LB");

        _factory.Profile
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerProfileResult.Found(
                player, new PlayerPrice(11_000_000, "ISK"), 128.0,
                new AggregatedStats(
                    Games: 10, Goals: 20, YellowCards: 1, TwoMinuteSuspensions: 0, RedCards: 0,
                    Assists: 5, Steals: 3, Blocks: 1, Saves: 0, Turnovers: 2, LegalStops: 1,
                    Shots: 30, ExpectedGoals: 18.5, ShotsFaced: 0, SavePct: null, ExpectedSaves: 0,
                    GradeTotal: 7.2, GradeOffense: 7.5, GradeDefense: 6.9, GradeGoalkeeping: null)));

        var response = await _client.GetAsync("/api/players/12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("12345", body.GetProperty("playerId").GetString());
        Assert.Equal("Aron Pálmarsson", body.GetProperty("name").GetString());
        Assert.Equal("Stjarnan", body.GetProperty("clubName").GetString());
        Assert.Equal("karlar", body.GetProperty("gender").GetString());
        Assert.Equal(35, body.GetProperty("age").GetInt32());
        Assert.Equal("VS", body.GetProperty("position").GetString());
        Assert.Equal("LB", body.GetProperty("positionSecondary").GetString());
        var price = body.GetProperty("price");
        Assert.Equal(11_000_000, price.GetProperty("amount").GetDouble());
        Assert.Equal("ISK", price.GetProperty("currency").GetString());
        Assert.Equal(128.0, body.GetProperty("rating").GetDouble());
        Assert.Equal(10, body.GetProperty("games").GetInt32());
        Assert.Equal(20, body.GetProperty("goals").GetInt32());
        Assert.Equal(5, body.GetProperty("assists").GetInt32());
        Assert.Equal(3, body.GetProperty("steals").GetInt32());
        Assert.Equal(18.5, body.GetProperty("expectedGoals").GetDouble());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("savePct").ValueKind);
        Assert.Equal(7.2, body.GetProperty("gradeTotal").GetDouble());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("gradeGoalkeeping").ValueKind);
    }

    [Fact]
    public async Task GetPlayer_NoGamesInScope_Returns200WithRatingZero()
    {
        // The "0 for no games" rule lives upstream (PlayerPriceService); this only pins that a 0.0 rating round-trips in the JSON.
        var player = new Player(
            "12345", "Aron Pálmarsson", "23",
            new DateOnly(1990, 7, 19),
            35, "385-karlar", "385", "Stjarnan", "karlar", "VS", false);

        _factory.Profile
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerProfileResult.Found(
                player, new PlayerPrice(5_000_000, "ISK"), 0.0, new AggregatedStats(0, 0, 0, 0, 0)));

        var response = await _client.GetAsync("/api/players/12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.0, body.GetProperty("rating").GetDouble());
    }

    [Fact]
    public async Task GetPlayer_RuleSetMissing_Returns200WithNullRating()
    {
        var player = new Player(
            "12345", "Aron Pálmarsson", "23",
            new DateOnly(1990, 7, 19),
            35, "385-karlar", "385", "Stjarnan", "karlar", "VS", false);

        _factory.Profile
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerProfileResult.Found(
                player, null, null, new AggregatedStats(0, 0, 0, 0, 0)));

        var response = await _client.GetAsync("/api/players/12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("rating").ValueKind);
    }

    [Fact]
    public async Task GetPlayer_RetiredPlayer_Returns200WithRetiredTrue()
    {
        var player = new Player(
            "12345", "Retired Rúnar", "23",
            new DateOnly(1985, 7, 19),
            40, "385-karlar", "385", "Stjarnan", "karlar", "VS", true);

        _factory.Profile
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerProfileResult.Found(
                player, new PlayerPrice(5_000_000, "ISK"), 50.0, new AggregatedStats(0, 0, 0, 0, 0)));

        var response = await _client.GetAsync("/api/players/12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("retired").GetBoolean());
    }

    [Fact]
    public async Task GetStats_PlayerNotFound_Returns404()
    {
        _factory.Stats
            .Setup(s => s.ExecuteAsync("nope", It.IsAny<PlayerStatsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerStatsResult.NotFound());

        var response = await _client.GetAsync("/api/players/nope/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_TournamentIdAndCompetitionId_Returns400()
    {
        var response = await _client.GetAsync(
            "/api/players/p1/stats?season=2025-26&tournamentId=8427&competitionId=olis-karla");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_InvalidType_Returns400()
    {
        var response = await _client.GetAsync("/api/players/p1/stats?type=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_PlayerExistsNoStats_Returns200WithEmptyArray()
    {
        _factory.Stats
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<PlayerStatsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerStatsResult.Found("12345", Array.Empty<PlayerMatchStat>()));

        var response = await _client.GetAsync("/api/players/12345/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("12345", body.GetProperty("playerId").GetString());
        Assert.Equal(0, body.GetProperty("stats").GetArrayLength());
    }

    [Fact]
    public async Task GetStats_Line_IncludesStatFieldsOpponentDateAndPoints()
    {
        var stat = new PlayerStat("12345", "m1", "8444", "Olís deild karla", "2025-26", "453-karlar", "Valur",
            5, 0, 1, 0, HbStatzAssists: 3);
        var line = new PlayerMatchStat(stat, new DateTimeOffset(2025, 9, 1, 19, 30, 0, TimeSpan.Zero),
            new MatchListTeam("143-karlar", "143", "Haukar", null, 28), 12);
        _factory.Stats
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<PlayerStatsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerStatsResult.Found("12345", new[] { line }));

        var response = await _client.GetAsync("/api/players/12345/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("stats")[0];
        Assert.Equal("m1", row.GetProperty("matchId").GetString());
        Assert.Equal(5, row.GetProperty("goals").GetInt32());
        Assert.Equal(3, row.GetProperty("hbStatzAssists").GetInt32());
        Assert.Equal("143", row.GetProperty("opponentClubId").GetString());
        Assert.Equal("Haukar", row.GetProperty("opponentClubName").GetString());
        Assert.Equal(12, row.GetProperty("points").GetDouble());
        Assert.True(row.TryGetProperty("date", out _));
    }

    [Fact]
    public async Task CorsPreflight_AllowedOrigin_ReturnsAllowOriginHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/players/12345");
        request.Headers.Add("Origin", "http://localhost:5500");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"),
            "Expected Access-Control-Allow-Origin header on preflight response");
        Assert.Equal("http://localhost:5500",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
