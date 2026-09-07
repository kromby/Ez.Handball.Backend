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

public class AdminPlayersMissingPositionEndpointTests : IClassFixture<AdminPlayersMissingPositionEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetPlayersMissingPositionUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetPlayersMissingPositionUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminPlayersMissingPositionEndpointTests(Factory factory)
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

    private static Player SamplePlayer(string id = "1") => new(
        PlayerId: id, Name: "Aron Pálmarsson", JerseyNumber: "23", DateOfBirth: null, Age: null,
        TeamId: "385-karlar", ClubId: "385", ClubName: "Stjarnan", Gender: "karlar",
        Position: "Leikmaður", Retired: false);

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/players/missing-position");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(
            AuthedGet(TokenFor(isAdmin: false), "/api/admin/players/missing-position"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AdminToken_Returns200WithExpectedShape()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player> { SamplePlayer() });

        var response = await _client.SendAsync(
            AuthedGet(TokenFor(isAdmin: true), "/api/admin/players/missing-position"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var player = body[0];
        Assert.Equal("1", player.GetProperty("playerId").GetString());
        Assert.Equal("Aron Pálmarsson", player.GetProperty("name").GetString());
        Assert.Equal("385", player.GetProperty("clubId").GetString());
        Assert.Equal("Stjarnan", player.GetProperty("clubName").GetString());
        Assert.Equal("karlar", player.GetProperty("gender").GetString());
        Assert.Equal("Leikmaður", player.GetProperty("position").GetString());
    }

    [Fact]
    public async Task Get_AdminToken_ClubIdAndGenderQueryParams_PassesThroughToUseCase()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync("385", "karlar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player>());

        var response = await _client.SendAsync(AuthedGet(
            TokenFor(isAdmin: true), "/api/admin/players/missing-position?clubId=385&gender=karlar"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync("385", "karlar", It.IsAny<CancellationToken>()), Times.Once);
    }
}
