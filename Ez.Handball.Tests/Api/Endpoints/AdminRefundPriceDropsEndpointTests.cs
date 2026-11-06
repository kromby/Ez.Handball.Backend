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

public class AdminRefundPriceDropsEndpointTests
    : IClassFixture<AdminRefundPriceDropsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IRefundPriceDropsUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IRefundPriceDropsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminRefundPriceDropsEndpointTests(Factory factory)
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

    private const string Path = "/api/admin/prices/refund-drops";

    private static RefundPriceDropsResult Done(int rebased, double refunded, params string[] failures) =>
        new RefundPriceDropsResult.Done(new RefundPriceDropsReport(3, rebased, refunded, failures));

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var response = await _client.PostAsync(Path, null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: false), Path));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_NoQueryParam_DefaultsToDryRun()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(Done(2, 8_000_000));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), Path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("teamsScanned").GetInt32());
        Assert.Equal(2, body.GetProperty("playersRebased").GetInt32());
        Assert.Equal(8_000_000, body.GetProperty("totalRefunded").GetDouble());
        Assert.True(body.GetProperty("dryRun").GetBoolean());
        _factory.Uc.Verify(u => u.ExecuteAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_DryRunFalse_RunsForReal_AndListsFailures()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Done(1, 5_000_000, "u-2:fantasy/p-9"));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), Path + "?dryRun=false"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("dryRun").GetBoolean());
        Assert.Equal("u-2:fantasy/p-9", Assert.Single(body.GetProperty("failures").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Post_MissingRuleSet_Returns400()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RefundPriceDropsResult.RuleSetNotFound.Instance);

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), Path));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
