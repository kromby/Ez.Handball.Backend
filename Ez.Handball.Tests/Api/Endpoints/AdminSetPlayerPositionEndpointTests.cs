using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class AdminSetPlayerPositionEndpointTests : IClassFixture<AdminSetPlayerPositionEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<ISetPlayerPositionUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(ISetPlayerPositionUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminSetPlayerPositionEndpointTests(Factory factory)
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

    private HttpRequestMessage AuthedPost(string token, string path, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/admin/players/1/position", new { position = "GK" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(AuthedPost(
            TokenFor(isAdmin: false), "/api/admin/players/1/position", new { position = "GK" }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdminToken_ValidPosition_Returns200_AndPassesArgumentsThrough()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("1", "CB", "LB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SetPlayerPositionResult.Ok.Instance);

        var response = await _client.SendAsync(AuthedPost(
            TokenFor(isAdmin: true), "/api/admin/players/1/position",
            new { position = "CB", positionSecondary = "LB" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync("1", "CB", "LB", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_AdminToken_InvalidPosition_Returns400()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("1", "XX", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SetPlayerPositionResult.InvalidPosition.Instance);

        var response = await _client.SendAsync(AuthedPost(
            TokenFor(isAdmin: true), "/api/admin/players/1/position", new { position = "XX" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_position", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_AdminToken_PlayerNotFound_Returns404()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("nope", "GK", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SetPlayerPositionResult.PlayerNotFound.Instance);

        var response = await _client.SendAsync(AuthedPost(
            TokenFor(isAdmin: true), "/api/admin/players/nope/position", new { position = "GK" }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }
}
