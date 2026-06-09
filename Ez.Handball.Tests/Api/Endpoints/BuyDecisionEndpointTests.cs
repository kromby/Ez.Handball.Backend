using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

[Collection("Azurite")]
public class BuyDecisionEndpointTests : IClassFixture<BuyDecisionEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetBuyDecisionUseCase> Uc { get; } = new();

        static Factory()
        {
            Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Jwt:SigningKey"] = "integration-test-signing-key-32-bytes-min!!",
                    ["Jwt:Issuer"] = "ez-handball",
                    ["Jwt:Audience"] = "ez-handball-web",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Auth:RateLimit:PermitLimit"] = "1000",
                    ["Auth:RateLimit:SensitivePermitLimit"] = "1000"
                }));
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetBuyDecisionUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public BuyDecisionEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var clubs = _tables.GetTableClient(Tables.Clubs);
        await clubs.CreateIfNotExistsAsync();
        await clubs.UpsertEntityAsync(new ClubEntity { PartitionKey = "club", RowKey = "385", Name = "Stjarnan" });
    }

    public async Task DisposeAsync()
    {
        foreach (var t in new[] { Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens, Tables.EmailTokens, Tables.Clubs, Tables.GameTeamNameIndex })
        {
            try { await _tables.GetTableClient(t).DeleteAsync(); } catch { /* not created */ }
        }
    }

    private static string NewEmail() => $"u{Guid.NewGuid():N}@test.is";

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385", teamName = $"Test {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage Authed(string url, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static BuyDecision Allowed() =>
        new("p1", "fantasy", true, new PlayerPrice(20_000_000, "ISK"),
            System.Array.Empty<BuyRuleViolation>(), "fantasy-price-v1");

    private static BuyDecision Rejected() =>
        new("p1", "fantasy", false, new PlayerPrice(42_000_000, "ISK"),
            new[] { new BuyRuleViolation("insufficient_budget", "Cost exceeds remaining budget") }, "fantasy-price-v1");

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/players/p1/buy");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Allowed_Returns200WithExpectedShape()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), "p1", GameFlavor.Fantasy, It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyDecisionResult.Decided(Allowed()));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/players/p1/buy", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p1", body.GetProperty("playerId").GetString());
        Assert.Equal("fantasy", body.GetProperty("flavor").GetString());
        Assert.True(body.GetProperty("allowed").GetBoolean());
        Assert.Equal(20_000_000, body.GetProperty("cost").GetProperty("amount").GetDouble());
        Assert.Equal("ISK", body.GetProperty("cost").GetProperty("currency").GetString());
        Assert.Equal(0, body.GetProperty("violations").GetArrayLength());
        Assert.Equal("fantasy-price-v1", body.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Rejected_Returns200WithViolations()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), "p1", It.IsAny<GameFlavor>(), It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyDecisionResult.Decided(Rejected()));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/players/p1/buy", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("allowed").GetBoolean());
        Assert.Equal("insufficient_budget", body.GetProperty("violations")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task PlayerNotFound_Returns404()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), "ghost", It.IsAny<GameFlavor>(), It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyDecisionResult.PlayerNotFound());
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/players/ghost/buy", token));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvalidFlavor_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/players/p1/buy?flavor=bogus", token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RuleSetNotFound_Returns400()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), "p1", It.IsAny<GameFlavor>(), It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyDecisionResult.RuleSetNotFound());
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/players/p1/buy?ruleSetVersion=9", token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }
}
