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

public class MatchEndpointTests : IClassFixture<MatchEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetMatchUseCase> Match { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetMatchUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Match.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public MatchEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Match.Reset();
        _client = _factory.CreateClient();
    }

    private static MatchDetail SampleMatch() => new(
        MatchId: "103414", TournamentId: "8444", TournamentName: "Olís deild karla", Season: "2025-26",
        Date: DateTimeOffset.UnixEpoch, Venue: "Ásgarður", Attendance: 412, Status: "S",
        HomeTeam: new MatchTeam("385-karlar", "385", "Stjarnan",
            new LineScore(14, 14, 28),
            new[] { new MatchPlayerLine("9912", "Jón", "7", "VS", 6, 1, 1, 0) }),
        AwayTeam: new MatchTeam("390-karlar", "390", "Breiðablik",
            new LineScore(12, 13, 25), Array.Empty<MatchPlayerLine>()));

    [Fact]
    public async Task GetMatch_NotFound_Returns404WithErrorJson()
    {
        _factory.Match
            .Setup(s => s.ExecuteAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetMatchResult.NotFound());

        var response = await _client.GetAsync("/api/matches/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("match_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetMatch_Found_Returns200WithExpectedShape()
    {
        _factory.Match
            .Setup(s => s.ExecuteAsync("103414", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetMatchResult.Found(SampleMatch()));

        var response = await _client.GetAsync("/api/matches/103414");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("103414", body.GetProperty("matchId").GetString());
        Assert.Equal("Olís deild karla", body.GetProperty("tournamentName").GetString());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("attendance").ValueKind);

        var home = body.GetProperty("homeTeam");
        Assert.Equal("Stjarnan", home.GetProperty("clubName").GetString());
        Assert.Equal(28, home.GetProperty("score").GetProperty("final").GetInt32());
        Assert.Equal(JsonValueKind.Number, home.GetProperty("score").GetProperty("final").ValueKind);
        Assert.Equal(1, home.GetProperty("players").GetArrayLength());
        Assert.Equal("Jón", home.GetProperty("players")[0].GetProperty("name").GetString());

        Assert.Equal(0, body.GetProperty("awayTeam").GetProperty("players").GetArrayLength());
    }

    [Fact]
    public async Task GetMatch_NullVenueAndAttendance_SerializeAsNull()
    {
        var match = SampleMatch() with { Venue = null, Attendance = null };
        _factory.Match
            .Setup(s => s.ExecuteAsync("103414", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetMatchResult.Found(match));

        var response = await _client.GetAsync("/api/matches/103414");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("venue").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attendance").ValueKind);
    }
}
