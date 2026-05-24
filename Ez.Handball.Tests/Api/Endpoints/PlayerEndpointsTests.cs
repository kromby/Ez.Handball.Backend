using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Api.Models;
using Ez.Handball.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class PlayerEndpointsTests : IClassFixture<PlayerEndpointsTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IPlayerLookupService> Lookup { get; } = new();
        public Mock<IPlayerStatsService> Stats { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                Replace(services, Lookup.Object);
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
        _factory.Lookup.Reset();
        _factory.Stats.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetPlayer_NotFound_Returns404WithErrorJson()
    {
        _factory.Lookup
            .Setup(s => s.GetPlayerAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerProfile?)null);

        var response = await _client.GetAsync("/api/players/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetPlayer_Existing_Returns200WithProfile()
    {
        var profile = new PlayerProfile(
            "12345", "Aron Pálmarsson", "23",
            new DateTimeOffset(1990, 7, 19, 0, 0, 0, TimeSpan.Zero),
            35, "385-karlar", "385", "Stjarnan", "karlar");

        _factory.Lookup
            .Setup(s => s.GetPlayerAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var response = await _client.GetAsync("/api/players/12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("12345", body.GetProperty("playerId").GetString());
        Assert.Equal("Aron Pálmarsson", body.GetProperty("name").GetString());
        Assert.Equal("Stjarnan", body.GetProperty("clubName").GetString());
        Assert.Equal("karlar", body.GetProperty("gender").GetString());
        Assert.Equal(35, body.GetProperty("age").GetInt32());
    }

    [Fact]
    public async Task GetStats_PlayerNotFound_Returns404()
    {
        _factory.Lookup
            .Setup(s => s.GetPlayerAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerProfile?)null);

        var response = await _client.GetAsync("/api/players/nope/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_PlayerExistsNoStats_Returns200WithEmptyArray()
    {
        var profile = new PlayerProfile(
            "12345", "X", null, null, null, "385-karlar", "385", null, "karlar");

        _factory.Lookup
            .Setup(s => s.GetPlayerAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _factory.Stats
            .Setup(s => s.GetStatsAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlayerStatRow>());

        var response = await _client.GetAsync("/api/players/12345/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("12345", body.GetProperty("playerId").GetString());
        Assert.Equal(0, body.GetProperty("stats").GetArrayLength());
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
