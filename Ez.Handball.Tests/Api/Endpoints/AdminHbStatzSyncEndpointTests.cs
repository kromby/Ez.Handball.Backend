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

public class AdminHbStatzSyncEndpointTests : IClassFixture<AdminHbStatzSyncEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<ITriggerHbStatzSyncUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(ITriggerHbStatzSyncUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminHbStatzSyncEndpointTests(Factory factory)
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

    private static HttpRequestMessage AuthedPost(string token, string path) =>
        new(HttpMethod.Post, path) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var response = await _client.PostAsync("/api/admin/hbstatz-sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: false), "/api/admin/hbstatz-sync"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdminToken_Success_Returns200WithCounts()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HbStatzSyncTriggerResult(true, 5, 4, new List<string> { "999" }, Array.Empty<string>(), null));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), "/api/admin/hbstatz-sync"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, body.GetProperty("matchesChecked").GetInt32());
        Assert.Equal(4, body.GetProperty("matchesSynced").GetInt32());
        Assert.Equal("999", body.GetProperty("unmatched")[0].GetString());
    }

    [Fact]
    public async Task Post_AdminToken_PassesTournamentIdQueryParamThrough()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync("9142", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HbStatzSyncTriggerResult(true, 0, 0, Array.Empty<string>(), Array.Empty<string>(), null));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), "/api/admin/hbstatz-sync?tournamentId=9142"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync("9142", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_AdminToken_PassesRoundQueryParamThrough()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync("9142", "3", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HbStatzSyncTriggerResult(true, 0, 0, Array.Empty<string>(), Array.Empty<string>(), null));

        var response = await _client.SendAsync(
            AuthedPost(TokenFor(isAdmin: true), "/api/admin/hbstatz-sync?tournamentId=9142&round=3"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync("9142", "3", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_AdminToken_PassesMatchIdQueryParamThrough()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync("9142", null, "103414", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HbStatzSyncTriggerResult(true, 1, 1, Array.Empty<string>(), Array.Empty<string>(), null));

        var response = await _client.SendAsync(
            AuthedPost(TokenFor(isAdmin: true), "/api/admin/hbstatz-sync?tournamentId=9142&matchId=103414"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync("9142", null, "103414", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_AdminToken_IngestionUnreachable_Returns502()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HbStatzSyncTriggerResult(false, 0, 0, Array.Empty<string>(), Array.Empty<string>(), "ingestion_unreachable"));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), "/api/admin/hbstatz-sync"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ingestion_unreachable", body.GetProperty("error").GetString());
    }
}
