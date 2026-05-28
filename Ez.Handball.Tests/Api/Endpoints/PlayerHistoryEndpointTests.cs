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

public class PlayerHistoryEndpointTests : IClassFixture<PlayerHistoryEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetPlayerHistoryUseCase> History { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetPlayerHistoryUseCase));
                services.Remove(descriptor);
                services.AddSingleton(History.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public PlayerHistoryEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.History.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetHistory_PlayerNotFound_Returns404WithErrorJson()
    {
        _factory.History
            .Setup(s => s.ExecuteAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerHistoryResult.NotFound());

        var response = await _client.GetAsync("/api/players/nope/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetHistory_PlayerWithEntries_Returns200WithExpectedShape()
    {
        var entry = new PlayerHistoryEntry(
            Season: "2025", TournamentId: "8444", TournamentName: "Olís deild karla",
            ClubId: "385", ClubName: "Stjarnan",
            Games: 18, TotalGoals: 87, TotalYellowCards: 4, TotalTwoMinuteSuspensions: 17, TotalRedCards: 0,
            AvgGoals: 87.0 / 18, AvgYellowCards: 4.0 / 18, AvgTwoMinuteSuspensions: 17.0 / 18, AvgRedCards: 0);
        var totals = new PlayerHistoryTotals(
            18, 87, 4, 17, 0, 87.0 / 18, 4.0 / 18, 17.0 / 18, 0);
        var wrapper = new PlayerHistory(new[] { entry }, totals);

        _factory.History
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerHistoryResult.Found("12345", wrapper));

        var response = await _client.GetAsync("/api/players/12345/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("12345", body.GetProperty("playerId").GetString());
        Assert.Equal(1, body.GetProperty("history").GetArrayLength());

        var first = body.GetProperty("history")[0];
        Assert.Equal("2025", first.GetProperty("season").GetString());
        Assert.Equal("Olís deild karla", first.GetProperty("tournamentName").GetString());
        Assert.Equal(18, first.GetProperty("games").GetInt32());
        Assert.Equal(JsonValueKind.Number, first.GetProperty("avgGoals").ValueKind);

        var bodyTotals = body.GetProperty("totals");
        Assert.Equal(JsonValueKind.Object, bodyTotals.ValueKind);
        Assert.Equal(18, bodyTotals.GetProperty("games").GetInt32());
        Assert.Equal(JsonValueKind.Number, bodyTotals.GetProperty("avgGoals").ValueKind);
    }

    [Fact]
    public async Task GetHistory_PlayerExistsNoMatches_Returns200WithEmptyHistoryAndNullTotals()
    {
        var wrapper = new PlayerHistory(Array.Empty<PlayerHistoryEntry>(), null);
        _factory.History
            .Setup(s => s.ExecuteAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPlayerHistoryResult.Found("12345", wrapper));

        var response = await _client.GetAsync("/api/players/12345/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("history").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("totals").ValueKind);
    }
}
