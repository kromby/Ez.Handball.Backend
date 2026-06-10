using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Api.Endpoints;

public class RoundsEndpointTests : IClassFixture<RoundsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetRoundsUseCase> Rounds { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetRoundsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Rounds.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public RoundsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Rounds.Reset();
        _client = _factory.CreateClient();
    }

    private static RoundListing SampleListing() => new(
        "8444", "Olís deild karla", "2025-26",
        new[]
        {
            new RoundGroup("1", new DateOnly(2025, 9, 3), new DateOnly(2025, 9, 3),
                new[]
                {
                    new RoundMatch("5001", true, DateTimeOffset.UnixEpoch, "Höllin",
                        new RoundTeam("385-karlar", "385", "KR", "logo-385", 28),
                        new RoundTeam("390-karlar", "390", "Breiðablik", null, 25))
                })
        });

    [Fact]
    public async Task GetRounds_Found_Returns200WithListing()
    {
        _factory.Rounds
            .Setup(s => s.ExecuteAsync("8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRoundsResult.Found(SampleListing()));

        var response = await _client.GetAsync("/api/tournaments/8444/rounds");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("8444", root.GetProperty("tournamentId").GetString());
        var round = root.GetProperty("rounds")[0];
        Assert.Equal("1", round.GetProperty("round").GetString());
        Assert.Equal("2025-09-03", round.GetProperty("startDate").GetString());
        var match = round.GetProperty("matches")[0];
        Assert.True(match.GetProperty("played").GetBoolean());
        Assert.Equal(28, match.GetProperty("home").GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task GetRounds_NotFound_Returns404WithErrorJson()
    {
        _factory.Rounds
            .Setup(s => s.ExecuteAsync("9999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRoundsResult.NotFound());

        var response = await _client.GetAsync("/api/tournaments/9999/rounds");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tournament_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetRounds_WhitespaceTournamentId_Returns400WithErrorJson()
    {
        var response = await _client.GetAsync("/api/tournaments/%20/rounds");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_tournament_id", body.GetProperty("error").GetString());
    }
}
