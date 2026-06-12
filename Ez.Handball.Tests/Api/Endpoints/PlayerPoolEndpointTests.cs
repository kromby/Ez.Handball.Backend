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

public class PlayerPoolEndpointTests : IClassFixture<PlayerPoolEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        static Factory()
        {
            // Program eagerly reads Jwt:SigningKey while building the host; supply it
            // via env var so this factory builds in isolation (known gotcha #69).
            Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-test-signing-key-32-bytes-min!!");
        }

        public Mock<IGetPlayerPoolUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetPlayerPoolUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlayerPoolEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private static PlayerPool EmptyPool(string sort = "Rating") =>
        new(sort, 0, 0, 50, Array.Empty<PlayerPoolEntry>());

    private void SetupFound(PlayerPool pool) =>
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerPoolResult.Found(pool));

    [Fact]
    public async Task Get_NoParams_DefaultsGoalsSortOffset0Limit50Version1()
    {
        PlayerPoolRequest? captured = null;
        var capturedOffset = -1;
        var capturedLimit = -1;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerPoolRequest, int, int, CancellationToken>((r, o, l, _) =>
            {
                captured = r; capturedOffset = o; capturedLimit = l;
            })
            .ReturnsAsync(new PlayerPoolResult.Found(EmptyPool()));

        var response = await _client.GetAsync("/api/players");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(PlayerPoolSort.Goals, captured!.Sort);
        Assert.Equal(1, captured.PriceVersion);
        Assert.Null(captured.Position);
        Assert.Equal(0, capturedOffset);
        Assert.Equal(50, capturedLimit);
    }

    [Fact]
    public async Task Get_PassesFiltersAndSortThrough()
    {
        PlayerPoolRequest? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerPoolRequest, int, int, CancellationToken>((r, _, _, _) => captured = r)
            .ReturnsAsync(new PlayerPoolResult.Found(EmptyPool("Price")));

        var response = await _client.GetAsync(
            "/api/players?season=2025-26&gender=karlar&position=CB&sort=price&version=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2025-26", captured!.Season);
        Assert.Equal("karlar", captured.Gender);
        Assert.Equal("CB", captured.Position);
        Assert.Equal(PlayerPoolSort.Price, captured.Sort);
        Assert.Equal(2, captured.PriceVersion);
    }

    [Fact]
    public async Task Get_PassesNameAndClubIdThrough()
    {
        PlayerPoolRequest? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerPoolRequest, int, int, CancellationToken>((r, _, _, _) => captured = r)
            .ReturnsAsync(new PlayerPoolResult.Found(EmptyPool()));

        var response = await _client.GetAsync("/api/players?name=berg%20str%C3%B6m&clubId=385");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("berg ström", captured!.Name);
        Assert.Equal("385", captured.ClubId);
    }

    [Fact]
    public async Task Get_SortPickPercentage_Accepted()
    {
        SetupFound(EmptyPool("PickPercentage"));

        var response = await _client.GetAsync("/api/players?sort=pickPercentage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_InvalidSort_Returns400()
    {
        var response = await _client.GetAsync("/api/players?sort=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_sort", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_InvalidGender_Returns400()
    {
        var response = await _client.GetAsync("/api/players?gender=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_TournamentIdAndCompetitionId_Returns400()
    {
        var response = await _client.GetAsync(
            "/api/players?tournamentId=8444&competitionId=olis-karla");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_scope", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_LimitTooLarge_Returns400()
    {
        var response = await _client.GetAsync("/api/players?limit=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_RuleSetNotFound_Returns400InvalidRuleSet()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerPoolResult.RuleSetNotFound());

        var response = await _client.GetAsync("/api/players");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_StatSort_AcceptedAndPassedThrough()
    {
        PlayerPoolRequest? captured = null;
        _factory.Uc
            .Setup(s => s.ExecuteAsync(
                It.IsAny<PlayerPoolRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerPoolRequest, int, int, CancellationToken>((r, _, _, _) => captured = r)
            .ReturnsAsync(new PlayerPoolResult.Found(EmptyPool("Goals")));

        var response = await _client.GetAsync("/api/players?sort=redCards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PlayerPoolSort.RedCards, captured!.Sort);
    }
}
