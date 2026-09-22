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

public class AdminSettleGameweeksEndpointTests : IClassFixture<AdminSettleGameweeksEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<ISettleRoundForAllTeamsUseCase> SettleRound { get; } = new();
        public Mock<ISettleCompletedRoundsUseCase> SettleCompleted { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(ISettleRoundForAllTeamsUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(ISettleCompletedRoundsUseCase)));
                services.AddSingleton(SettleRound.Object);
                services.AddSingleton(SettleCompleted.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private const string Path = "/api/admin/gameweeks/settle";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminSettleGameweeksEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.SettleRound.Reset();
        _factory.SettleCompleted.Reset();
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
    public async Task Post_WithRound_SettlesThatRound()
    {
        _factory.SettleRound.Setup(u => u.ExecuteAsync("3", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettleRoundForAllTeamsResult.Completed(new SettleRoundReport("3", 4, 3, 0, 1)));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), $"{Path}?round=3"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("3", body.GetProperty("round").GetString());
        Assert.Equal(3, body.GetProperty("settled").GetInt32());
        _factory.SettleCompleted.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_UnknownRound_Returns404()
    {
        _factory.SettleRound.Setup(u => u.ExecuteAsync("99", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettleRoundForAllTeamsResult.RoundNotFound.Instance);

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), $"{Path}?round=99"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutRound_SettlesEveryCompleteRound()
    {
        _factory.SettleCompleted.Setup(u => u.ExecuteAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettleCompletedRoundsResult.Completed(new[]
            {
                new SettleRoundReport("1", 4, 4, 0, 0),
                new SettleRoundReport("2", 4, 4, 0, 0),
            }));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), Path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rounds = body.GetProperty("rounds").EnumerateArray().Select(r => r.GetProperty("round").GetString());
        Assert.Equal(new[] { "1", "2" }, rounds);
        _factory.SettleRound.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_WithoutRound_RoundFailure_MapsTheReason()
    {
        _factory.SettleCompleted.Setup(u => u.ExecuteAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettleCompletedRoundsResult.RoundFailed("1", SettleRoundForAllTeamsResult.RuleSetMissing.Instance));

        var response = await _client.SendAsync(AuthedPost(TokenFor(isAdmin: true), Path));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
