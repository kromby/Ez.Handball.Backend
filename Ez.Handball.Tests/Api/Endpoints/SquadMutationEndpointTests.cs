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
public class SquadMutationEndpointTests : IClassFixture<SquadMutationEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IBuyPlayerUseCase> Buy { get; } = new();
        public Mock<ISellPlayerUseCase> Sell { get; } = new();

        static Factory() => Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["Jwt:SigningKey"] = "integration-test-signing-key-32-bytes-min!!",
                ["Jwt:Issuer"] = "ez-handball", ["Jwt:Audience"] = "ez-handball-web",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Auth:RateLimit:PermitLimit"] = "1000", ["Auth:RateLimit:SensitivePermitLimit"] = "1000"
            }));
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(IBuyPlayerUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(ISellPlayerUseCase)));
                services.AddSingleton(Buy.Object);
                services.AddSingleton(Sell.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public SquadMutationEndpointTests(Factory factory)
    {
        _factory = factory; _factory.Buy.Reset(); _factory.Sell.Reset();
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
        foreach (var t in new[] { Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens, Tables.EmailTokens, Tables.Clubs, Tables.GameTeams, Tables.GameBudgets, Tables.GameRosters })
            try { await _tables.GetTableClient(t).DeleteAsync(); } catch { }
    }

    private static string NewEmail() => $"u{Guid.NewGuid():N}@test.is";

    private async Task<string> TokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385", teamName = "Test Team" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage Req(HttpMethod m, string url, string token, object? json = null)
    {
        var req = new HttpRequestMessage(m, url) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };
        if (json is not null) req.Content = JsonContent.Create(json);
        return req;
    }

    private static SquadView View() => new(
        new[] { new SquadPlayer("p-1", "Aron", "385", "Stjarnan", "VS", "karlar", new PlayerCost(50_000_000, "ISK"), new PlayerCost(42_000_000, "ISK")) },
        new PlayerCost(42_000_000, "ISK"), new PlayerCost(8_000_000, "ISK"), new PlayerCost(50_000_000, "ISK"));

    [Fact]
    public async Task Buy_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/users/me/squad/players", new { playerId = "p-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Buy_Committed_Returns201WithSquadView()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyPlayerResult.Committed(View()));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "fantasy" }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p-1", body.GetProperty("players")[0].GetProperty("playerId").GetString());
        Assert.Equal(8_000_000, body.GetProperty("remainingBudget").GetProperty("amount").GetDouble());
    }

    [Fact]
    public async Task Buy_Rejected_Returns422WithViolations()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyPlayerResult.Rejected(new[] { new BuyRuleViolation("insufficient_budget", "Cost exceeds remaining budget") }));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficient_budget", body.GetProperty("violations")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Buy_Duplicate_Returns409()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuyPlayerResult.Duplicate.Instance);
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1" }));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Buy_PlayerNotFound_Returns404()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuyPlayerResult.PlayerNotFound.Instance);
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1" }));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Buy_InvalidFlavor_Returns400()
    {
        var token = await TokenAsync();
        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "manager" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sell_Sold_Returns200WithSquadView()
    {
        _factory.Sell.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SellPlayerResult.Sold(View()));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Delete, "/api/users/me/squad/players/p-1?flavor=fantasy", token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Sell_NotInSquad_Returns404()
    {
        _factory.Sell.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SellPlayerResult.NotInSquad.Instance);
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Delete, "/api/users/me/squad/players/p-1", token));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Buy_NoTeam_Returns409()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuyPlayerResult.NoTeam.Instance);
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1" }));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no_team", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sell_NoTeam_Returns409()
    {
        _factory.Sell.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SellPlayerResult.NoTeam.Instance);
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Delete, "/api/users/me/squad/players/p-1", token));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no_team", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sell_RuleSetNotFound_Returns400()
    {
        _factory.Sell.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SellPlayerResult.RuleSetNotFound.Instance);
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Delete, "/api/users/me/squad/players/p-1?ruleSetVersion=9", token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sell_InvalidFlavor_Returns400()
    {
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Delete, "/api/users/me/squad/players/p-1?flavor=manager", token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sell_WithoutToken_Returns401()
    {
        var resp = await _client.DeleteAsync("/api/users/me/squad/players/p-1");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
