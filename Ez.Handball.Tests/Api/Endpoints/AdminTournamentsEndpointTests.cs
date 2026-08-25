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

public class AdminTournamentsEndpointTests : IClassFixture<AdminTournamentsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetTournamentStatusUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetTournamentStatusUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminTournamentsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    // Tokens are minted directly via ITokenService (a singleton, no table storage involved)
    // rather than through /api/auth/register — that always creates IsAdmin=false users, and
    // this endpoint never reads the Users table, so there's nothing an Azurite-backed flow adds.
    private string TokenFor(bool isAdmin) =>
        _factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(new UserEntity
        {
            RowKey = "u-1", Email = "a@b.is", DisplayName = "Jón", EmailVerified = true, IsAdmin = isAdmin
        });

    private static HttpRequestMessage AuthedGet(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/tournaments");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/tournaments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(AuthedGet(TokenFor(isAdmin: false)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AdminToken_Returns200WithExpectedShape()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new List<TournamentStatus>
            {
                new("8444", "Olís deild karla", "karlar", TournamentType.League,
                    "olis-karla", "Olís deild karla", "2025-26", true, true, 10)
            });

        var response = await _client.SendAsync(AuthedGet(TokenFor(isAdmin: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var first = body[0];
        Assert.Equal("8444", first.GetProperty("tournamentId").GetString());
        Assert.Equal("2025-26", first.GetProperty("season").GetString());
        Assert.True(first.GetProperty("active").GetBoolean());
        Assert.True(first.GetProperty("ingest").GetBoolean());
        Assert.Equal(10, first.GetProperty("priority").GetInt32());
    }
}
