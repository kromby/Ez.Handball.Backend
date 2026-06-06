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

public class PlayerValueEndpointTests : IClassFixture<PlayerValueEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetPlayerValueUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetPlayerValueUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlayerValueEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private static PlayerValue SampleValue() =>
        new("p1", "fantasy", 37,
            new[] { new PlayerValueComponent("goals", 18, 2, 36) },
            "fantasy-v1");

    [Fact]
    public async Task Get_DefaultFlavor_Returns200AndExpectedShape()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("p1", ValueFlavor.Fantasy, It.IsAny<PlayerValueContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerValueResult.Found(SampleValue()));

        var response = await _client.GetAsync("/api/players/p1/value");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p1", body.GetProperty("playerId").GetString());
        Assert.Equal("fantasy", body.GetProperty("flavor").GetString());
        Assert.Equal(37, body.GetProperty("value").GetDouble());
        Assert.Equal("fantasy-v1", body.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("components").ValueKind);
    }

    [Fact]
    public async Task Get_PassesParsedFlavorAndContextToUseCase()
    {
        PlayerValueContext? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync("p1", ValueFlavor.Manager, It.IsAny<PlayerValueContext>(), It.IsAny<CancellationToken>()))
            .Callback((string _, ValueFlavor _, PlayerValueContext c, CancellationToken _) => captured = c)
            .ReturnsAsync(new GetPlayerValueResult.Found(SampleValue()));

        var response = await _client.GetAsync(
            "/api/players/p1/value?flavor=manager&season=2025-26&tournamentId=8444&ruleSetVersion=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal("2025-26", captured!.Season);
        Assert.Equal("8444", captured.TournamentId);
        Assert.Equal(2, captured.RuleSetVersion);
    }

    [Fact]
    public async Task Get_InvalidFlavor_Returns400()
    {
        var response = await _client.GetAsync("/api/players/p1/value?flavor=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_PlayerNotFound_Returns404()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<ValueFlavor>(), It.IsAny<PlayerValueContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerValueResult.NotFound());

        var response = await _client.GetAsync("/api/players/ghost/value");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_RuleSetNotFound_Returns400()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<ValueFlavor>(), It.IsAny<PlayerValueContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerValueResult.RuleSetNotFound());

        var response = await _client.GetAsync("/api/players/p1/value?ruleSetVersion=99");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }
}
