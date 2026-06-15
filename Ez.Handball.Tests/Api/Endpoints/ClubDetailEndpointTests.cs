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

public class ClubDetailEndpointTests : IClassFixture<ClubDetailEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetClubUseCase> Club { get; } = new();
        public Mock<IGetClubRosterUseCase> Roster { get; } = new();
        public Mock<IGetClubMatchesUseCase> Matches { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                Replace(services, Club.Object);
                Replace(services, Roster.Object);
                Replace(services, Matches.Object);
            });
            return base.CreateHost(builder);
        }

        private static void Replace<T>(IServiceCollection services, T impl) where T : class
        {
            var d = services.Single(x => x.ServiceType == typeof(T));
            services.Remove(d);
            services.AddSingleton(impl);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ClubDetailEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Club.Reset();
        _factory.Roster.Reset();
        _factory.Matches.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetClub_Found_Returns200WithPlaceholders()
    {
        _factory.Club
            .Setup(s => s.ExecuteAsync("385", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetClubResult.Found(new Club("385", "KR", "https://logo/kr.png")));

        var response = await _client.GetAsync("/api/clubs/385");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("385", body.GetProperty("clubId").GetString());
        Assert.Equal("KR", body.GetProperty("name").GetString());
        Assert.Equal("https://logo/kr.png", body.GetProperty("logoUrl").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("venue").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("foundedYear").ValueKind);
    }

    [Fact]
    public async Task GetClub_NotFound_Returns404()
    {
        _factory.Club
            .Setup(s => s.ExecuteAsync("999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetClubResult.NotFound());

        var response = await _client.GetAsync("/api/clubs/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("club_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetClub_BlankId_Returns400()
    {
        var response = await _client.GetAsync("/api/clubs/%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_club_id", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetRoster_Found_Returns200WithPlayers()
    {
        _factory.Roster
            .Setup(s => s.ExecuteAsync("385", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetClubRosterResult.Found(new ClubRoster("385", "2025-2026",
                new List<ClubRosterPlayer> { new("p1", "Aron", "23", "VS", 35) })));

        var response = await _client.GetAsync("/api/clubs/385/roster");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("385", body.GetProperty("clubId").GetString());
        Assert.Equal("2025-2026", body.GetProperty("season").GetString());
        var players = body.GetProperty("players");
        Assert.Equal(1, players.GetArrayLength());
        Assert.Equal("Aron", players[0].GetProperty("name").GetString());
        Assert.Equal("23", players[0].GetProperty("jerseyNumber").GetString());
        Assert.Equal("VS", players[0].GetProperty("position").GetString());
        Assert.Equal(35, players[0].GetProperty("age").GetInt32());
    }

    [Fact]
    public async Task GetRoster_UnknownClub_Returns404()
    {
        _factory.Roster
            .Setup(s => s.ExecuteAsync("999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetClubRosterResult.NotFound());

        var response = await _client.GetAsync("/api/clubs/999/roster");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMatches_Found_Returns200WithClubPerspective()
    {
        _factory.Matches
            .Setup(s => s.ExecuteAsync("385", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetClubMatchesResult.Found(new ClubMatchListing("385", "2025-2026",
                new List<ClubMatch>
                {
                    new("m1", "8444", "Olís deild karla", "1",
                        new DateTimeOffset(2025, 9, 3, 19, 0, 0, TimeSpan.Zero), "Höllin",
                        "played", true, "390", "Breidablik", "logo-a", 28, 24)
                })));

        var response = await _client.GetAsync("/api/clubs/385/matches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("385", body.GetProperty("clubId").GetString());
        var matches = body.GetProperty("matches");
        Assert.Equal(1, matches.GetArrayLength());
        var m = matches[0];
        Assert.Equal("played", m.GetProperty("status").GetString());
        Assert.True(m.GetProperty("isHome").GetBoolean());
        Assert.Equal("390", m.GetProperty("opponentClubId").GetString());
        Assert.Equal(28, m.GetProperty("clubScore").GetInt32());
        Assert.Equal(24, m.GetProperty("opponentScore").GetInt32());
    }

    [Fact]
    public async Task GetMatches_StatusFilter_PassedToUseCase()
    {
        _factory.Matches
            .Setup(s => s.ExecuteAsync("385", ClubMatchStatusFilter.Upcoming, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetClubMatchesResult.Found(new ClubMatchListing("385", "2025-2026", new List<ClubMatch>())));

        var response = await _client.GetAsync("/api/clubs/385/matches?status=upcoming");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Matches.Verify(
            s => s.ExecuteAsync("385", ClubMatchStatusFilter.Upcoming, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMatches_InvalidStatus_Returns400()
    {
        var response = await _client.GetAsync("/api/clubs/385/matches?status=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMatches_UnknownClub_Returns404()
    {
        _factory.Matches
            .Setup(s => s.ExecuteAsync("999", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetClubMatchesResult.NotFound());

        var response = await _client.GetAsync("/api/clubs/999/matches");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
