using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class AdminGamesEndpointTests : IClassFixture<AdminGamesEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetAdminGameStatusUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetAdminGameStatusUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminGamesEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private string TokenFor(bool isAdmin) =>
        _factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(new UserEntity
        {
            RowKey = "u-1", Email = "a@b.is", DisplayName = "Jón", EmailVerified = true, IsAdmin = isAdmin
        });

    private static HttpRequestMessage AuthedGet(string token, string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/games");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(AuthedGet(TokenFor(isAdmin: false), "/api/admin/games"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AdminToken_Returns200WithExpectedShape_AndPassesSeasonThrough()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync("2025-26", It.IsAny<CancellationToken>())).ReturnsAsync(
            new List<AdminTournamentGames>
            {
                new("8444", "Olís deild karla", "Olís deild karla", DateTimeOffset.UnixEpoch,
                    new List<AdminRoundGames>
                    {
                        new("1", new List<AdminGameStatus>
                        {
                            new("103414", DateTimeOffset.UnixEpoch, "Ásgarður", "Stjarnan", "Breiðablik", "played", true, true),
                            new("103415", DateTimeOffset.UnixEpoch, null, "Valur", "KA", "upcoming", false, false),
                        })
                    })
            });

        var response = await _client.SendAsync(AuthedGet(TokenFor(isAdmin: true), "/api/admin/games?season=2025-26"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var tournament = body[0];
        Assert.Equal("8444", tournament.GetProperty("tournamentId").GetString());
        var round = tournament.GetProperty("rounds")[0];
        Assert.Equal("1", round.GetProperty("round").GetString());
        var games = round.GetProperty("games");
        Assert.Equal("played", games[0].GetProperty("status").GetString());
        Assert.True(games[0].GetProperty("ingested").GetBoolean());
        Assert.False(games[1].GetProperty("ingested").GetBoolean());

        _factory.Uc.Verify(s => s.ExecuteAsync("2025-26", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_AdminToken_NoSeasonQueryParam_PassesNullToUseCase()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdminTournamentGames>());

        var response = await _client.SendAsync(AuthedGet(TokenFor(isAdmin: true), "/api/admin/games"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
