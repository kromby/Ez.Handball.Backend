using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class LeaderboardEndpointTests : IClassFixture<LeaderboardEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetLeaderboardUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetLeaderboardUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public LeaderboardEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private static Leaderboard EmptyLeaderboard(string metric = "Goals", int offset = 0, int limit = 50) =>
        new(metric, 0, offset, limit, Array.Empty<LeaderboardEntry>());

    [Fact]
    public async Task Get_NoParams_DefaultsToGoalsMetricOffset0Limit50()
    {
        LeaderboardQuery? captured = null;
        var capturedOffset = -1;
        var capturedLimit = -1;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<LeaderboardQuery>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<LeaderboardQuery, int, int, CancellationToken>((q, o, l, _) =>
            {
                captured = q; capturedOffset = o; capturedLimit = l;
            })
            .ReturnsAsync(EmptyLeaderboard());

        var response = await _client.GetAsync("/api/leaderboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(LeaderboardMetric.Goals, captured!.Metric);
        Assert.Null(captured.Season);
        Assert.Null(captured.TournamentId);
        Assert.Null(captured.Gender);
        Assert.Equal(0, capturedOffset);
        Assert.Equal(50, capturedLimit);
    }

    [Fact]
    public async Task Get_PassesFiltersThrough()
    {
        LeaderboardQuery? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<LeaderboardQuery>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<LeaderboardQuery, int, int, CancellationToken>((q, _, _, _) => captured = q)
            .ReturnsAsync(EmptyLeaderboard("YellowCards"));

        var response = await _client.GetAsync(
            "/api/leaderboard?metric=yellowCards&season=2025-26&tournamentId=8444&gender=kvenna");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LeaderboardMetric.YellowCards, captured!.Metric);
        Assert.Equal("2025-26", captured.Season);
        Assert.Equal("8444", captured.TournamentId);
        Assert.Equal("kvenna", captured.Gender);
    }

    [Fact]
    public async Task Get_FoundPage_Returns200WithExpectedShape()
    {
        var entry = new LeaderboardEntry(
            Rank: 1, PlayerId: "9912", Name: "Jón Jónsson",
            ClubId: "385", ClubName: "Stjarnan", Gender: "karlar",
            Games: 18, Goals: 142, YellowCards: 9, TwoMinuteSuspensions: 12, RedCards: 1, AvgGoals: 7.89);
        var board = new Leaderboard("Goals", 187, 0, 50, new[] { entry });

        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<LeaderboardQuery>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var response = await _client.GetAsync("/api/leaderboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Goals", body.GetProperty("metric").GetString());
        Assert.Equal(187, body.GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("offset").GetInt32());
        Assert.Equal(50, body.GetProperty("limit").GetInt32());
        Assert.Equal(1, body.GetProperty("entries").GetArrayLength());

        var first = body.GetProperty("entries")[0];
        Assert.Equal(1, first.GetProperty("rank").GetInt32());
        Assert.Equal("9912", first.GetProperty("playerId").GetString());
        Assert.Equal("Stjarnan", first.GetProperty("clubName").GetString());
        Assert.Equal(JsonValueKind.Number, first.GetProperty("goals").ValueKind);
        Assert.Equal(JsonValueKind.Number, first.GetProperty("avgGoals").ValueKind);
    }

    [Fact]
    public async Task Get_NullName_SerializesAsNull()
    {
        var entry = new LeaderboardEntry(
            1, "9912", null, "385", "Stjarnan", "karlar", 18, 142, 9, 12, 1, 7.89);
        var board = new Leaderboard("Goals", 1, 0, 50, new[] { entry });
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<LeaderboardQuery>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var response = await _client.GetAsync("/api/leaderboard");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("entries")[0].GetProperty("name").ValueKind);
    }

    [Fact]
    public async Task Get_InvalidMetric_Returns400_AndDoesNotCallUseCase()
    {
        var response = await _client.GetAsync("/api/leaderboard?metric=bananas");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_metric", body.GetProperty("error").GetString());
        _factory.Uc.Verify(s => s.ExecuteAsync(
            It.IsAny<LeaderboardQuery>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Get_InvalidGender_Returns400()
    {
        var response = await _client.GetAsync("/api/leaderboard?gender=other");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_gender", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_GenderCaseInsensitive_NormalizesToLowercase()
    {
        LeaderboardQuery? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<LeaderboardQuery>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<LeaderboardQuery, int, int, CancellationToken>((q, _, _, _) => captured = q)
            .ReturnsAsync(EmptyLeaderboard());

        var response = await _client.GetAsync("/api/leaderboard?gender=KARLAR");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("karlar", captured!.Gender);
    }

    [Theory]
    [InlineData("/api/leaderboard?offset=-1")]
    [InlineData("/api/leaderboard?limit=0")]
    [InlineData("/api/leaderboard?limit=201")]
    public async Task Get_InvalidPagination_Returns400(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_pagination", body.GetProperty("error").GetString());
    }
}
