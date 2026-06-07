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

public class PlayerRatingEndpointTests : IClassFixture<PlayerRatingEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetPlayerRatingUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetPlayerRatingUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlayerRatingEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private static PlayerRating SampleRating() =>
        new("p1", "fantasy", 37, new[] { new PlayerRatingComponent("goals", 18, 2, 36) }, "fantasy-v1");

    [Fact]
    public async Task Get_DefaultFlavor_Returns200AndExpectedShape()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("p1", GameFlavor.Fantasy, It.IsAny<PlayerRatingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerRatingResult.Found(SampleRating()));

        var response = await _client.GetAsync("/api/players/p1/rating");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p1", body.GetProperty("playerId").GetString());
        Assert.Equal("fantasy", body.GetProperty("flavor").GetString());
        Assert.Equal(37, body.GetProperty("rating").GetDouble());
        Assert.Equal("fantasy-v1", body.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("components").ValueKind);
    }

    [Fact]
    public async Task Get_PassesParsedFlavorAndContextToUseCase()
    {
        PlayerRatingContext? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync("p1", GameFlavor.Manager, It.IsAny<PlayerRatingContext>(), It.IsAny<CancellationToken>()))
            .Callback((string _, GameFlavor _, PlayerRatingContext c, CancellationToken _) => captured = c)
            .ReturnsAsync(new GetPlayerRatingResult.Found(SampleRating()));

        var response = await _client.GetAsync(
            "/api/players/p1/rating?flavor=manager&season=2025-26&tournamentId=8444&ruleSetVersion=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal("2025-26", captured!.Season);
        Assert.Equal("8444", captured.TournamentId);
        Assert.Equal(2, captured.RuleSetVersion);
    }

    [Fact]
    public async Task Get_InvalidFlavor_Returns400()
    {
        var response = await _client.GetAsync("/api/players/p1/rating?flavor=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_PlayerNotFound_Returns404()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<PlayerRatingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerRatingResult.NotFound());

        var response = await _client.GetAsync("/api/players/ghost/rating");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_RuleSetNotFound_Returns400()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<PlayerRatingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerRatingResult.RuleSetNotFound());

        var response = await _client.GetAsync("/api/players/p1/rating?ruleSetVersion=99");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }
}
