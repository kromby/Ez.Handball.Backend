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

public class AdminSyncEndpointTests : IClassFixture<AdminSyncEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<ITriggerIngestionSyncUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(ITriggerIngestionSyncUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminSyncEndpointTests(Factory factory)
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

    private static HttpRequestMessage AuthedPost(string token) =>
        new(HttpMethod.Post, "/api/admin/sync") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var response = await _client.PostAsync("/api/admin/sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: false)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AdminToken_Success_Returns200WithCounts()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(true, 6, new List<string> { "8437" }, null));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, body.GetProperty("synced").GetInt32());
        Assert.Equal("8437", body.GetProperty("failed")[0].GetString());
    }

    [Fact]
    public async Task Post_AdminToken_IngestionUnreachable_Returns502()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, 0, Array.Empty<string>(), "ingestion_unreachable"));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true)));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ingestion_unreachable", body.GetProperty("error").GetString());
    }
}
