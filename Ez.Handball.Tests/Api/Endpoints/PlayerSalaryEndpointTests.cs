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

public class PlayerSalaryEndpointTests : IClassFixture<PlayerSalaryEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetPlayerSalaryUseCase> Uc { get; } = new();

        static Factory()
        {
            Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetPlayerSalaryUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlayerSalaryEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private static PlayerSalary Sample() =>
        new("p1", new PlayerCost(20000000, "ISK"), 6, 8, "fantasy-price-v1");

    [Fact]
    public async Task Get_Returns200AndExpectedShape()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("p1", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerSalaryResult.Found(Sample()));

        var response = await _client.GetAsync("/api/players/p1/salary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p1", body.GetProperty("playerId").GetString());
        Assert.Equal(20000000, body.GetProperty("cost").GetProperty("amount").GetDouble());
        Assert.Equal("ISK", body.GetProperty("cost").GetProperty("currency").GetString());
        Assert.Equal(6, body.GetProperty("score").GetDouble());
        Assert.Equal(8, body.GetProperty("games").GetInt32());
        Assert.Equal("fantasy-price-v1", body.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Get_PassesParamsToUseCase()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("p1", 2, "2025-26", "8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerSalaryResult.Found(Sample()));

        var response = await _client.GetAsync("/api/players/p1/salary?season=2025-26&tournamentId=8444&ruleSetVersion=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync("p1", 2, "2025-26", "8444", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_PlayerNotFound_Returns404()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("ghost", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerSalaryResult.NotFound());

        var response = await _client.GetAsync("/api/players/ghost/salary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_RuleSetNotFound_Returns400()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("p1", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerSalaryResult.RuleSetNotFound());

        var response = await _client.GetAsync("/api/players/p1/salary?ruleSetVersion=9");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }
}
