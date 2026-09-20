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

public class AdminBackfillMiniLeagueMembershipIndexEndpointTests
    : IClassFixture<AdminBackfillMiniLeagueMembershipIndexEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IBackfillMiniLeagueMembershipIndexUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IBackfillMiniLeagueMembershipIndexUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminBackfillMiniLeagueMembershipIndexEndpointTests(Factory factory)
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

    private HttpRequestMessage AuthedPost(string token, string path) =>
        new(HttpMethod.Post, path) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var response = await _client.PostAsync("/api/admin/mini-leagues/backfill-membership-index", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(AuthedPost(
            TokenFor(isAdmin: false), "/api/admin/mini-leagues/backfill-membership-index"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_NoQueryParam_DefaultsToDryRun()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackfillMiniLeagueMembershipIndexResult(5, 0));

        var response = await _client.SendAsync(AuthedPost(
            TokenFor(isAdmin: true), "/api/admin/mini-leagues/backfill-membership-index"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, body.GetProperty("membersScanned").GetInt32());
        Assert.Equal(0, body.GetProperty("written").GetInt32());
        Assert.True(body.GetProperty("dryRun").GetBoolean());
        _factory.Uc.Verify(u => u.ExecuteAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_DryRunFalse_WritesAndReportsCounts()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackfillMiniLeagueMembershipIndexResult(5, 5));

        var response = await _client.SendAsync(AuthedPost(
            TokenFor(isAdmin: true), "/api/admin/mini-leagues/backfill-membership-index?dryRun=false"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, body.GetProperty("membersScanned").GetInt32());
        Assert.Equal(5, body.GetProperty("written").GetInt32());
        Assert.False(body.GetProperty("dryRun").GetBoolean());
    }
}
