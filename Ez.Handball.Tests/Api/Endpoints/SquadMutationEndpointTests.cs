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
        public Mock<IGetTransferAllowanceUseCase> Allowance { get; } = new();

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
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetTransferAllowanceUseCase)));
                services.AddSingleton(Allowance.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public SquadMutationEndpointTests(Factory factory)
    {
        _factory = factory; _factory.Buy.Reset(); _factory.Sell.Reset(); _factory.Allowance.Reset();
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
        foreach (var t in new[] { Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens, Tables.EmailTokens, Tables.Clubs, Tables.GameTeams, Tables.GameBudgets, Tables.GameRosters, Tables.GameTeamNameIndex })
            try { await _tables.GetTableClient(t).DeleteAsync(); } catch { }
    }

    private static string NewEmail() => $"u{Guid.NewGuid():N}@test.is";

    private async Task<string> TokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385", teamName = $"Test {Guid.NewGuid():N}" });
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
        new[] { new SquadPlayer("p-1", "Aron", "385", "Stjarnan", "VS", "karlar", new PlayerPrice(50_000_000, "ISK"), new PlayerPrice(42_000_000, "ISK"), Rating: 0.0) },
        new PlayerPrice(42_000_000, "ISK"), new PlayerPrice(8_000_000, "ISK"), new PlayerPrice(50_000_000, "ISK"));

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
        var squad = body.GetProperty("squad");
        Assert.Equal("p-1", squad.GetProperty("players")[0].GetProperty("playerId").GetString());
        Assert.Equal(8_000_000, squad.GetProperty("remainingBudget").GetProperty("amount").GetDouble());
        Assert.True(body.TryGetProperty("gameweek", out _));
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

    private void AllowanceAfterCommitIs(TransferAllowance allowance) =>
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTransferAllowanceResult.Found(allowance));

    // The allowance mock reports pre-commit numbers until the buy/sell mock has run, so these
    // tests fail if the endpoint ever reads the allowance before the transfer commits.
    private void AllowanceChangesWhenCommitted(TransferAllowance before, TransferAllowance after, Action<Action> onCommit)
    {
        var current = before;
        onCommit(() => current = after);
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new GetTransferAllowanceResult.Found(current));
    }

    [Fact]
    public async Task Buy_Committed_EchoesTheAllowanceIncludingThisBuy()
    {
        AllowanceChangesWhenCommitted(
            before: new TransferAllowance("7", 7, FreeAvailable: 1, Used: 1, Extra: 0, PointsHit: 0, Exempt: false),
            after: new TransferAllowance("7", 7, FreeAvailable: 1, Used: 2, Extra: 1, PointsHit: 4, Exempt: false),
            onCommit: commit => _factory.Buy
                .Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BuyPlayerResult.Committed(View()))
                .Callback(commit));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "fantasy" }));

        var transfers = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers");
        Assert.Equal(2, transfers.GetProperty("used").GetInt32());
        Assert.Equal(1, transfers.GetProperty("extra").GetInt32());
        Assert.Equal(4, transfers.GetProperty("pointsHit").GetInt32());
        Assert.Equal("7", transfers.GetProperty("roundLabel").GetString());
    }

    [Fact]
    public async Task Buy_Committed_UsingTheFreeTransfer_EchoesNoExtra()
    {
        AllowanceChangesWhenCommitted(
            before: new TransferAllowance("7", 7, FreeAvailable: 1, Used: 0, Extra: 0, PointsHit: 0, Exempt: false),
            after: new TransferAllowance("7", 7, FreeAvailable: 1, Used: 1, Extra: 0, PointsHit: 0, Exempt: false),
            onCommit: commit => _factory.Buy
                .Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BuyPlayerResult.Committed(View()))
                .Callback(commit));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "fantasy" }));

        var transfers = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers");
        Assert.Equal(1, transfers.GetProperty("used").GetInt32());
        Assert.Equal(0, transfers.GetProperty("extra").GetInt32());
        Assert.Equal(0, transfers.GetProperty("pointsHit").GetInt32());
    }

    [Fact]
    public async Task Buy_Committed_WhileBuildingTheSquad_EchoesExempt()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyPlayerResult.Committed(View()));
        AllowanceAfterCommitIs(new TransferAllowance("1", 1, FreeAvailable: 1, Used: 5, Extra: 0, PointsHit: 0, Exempt: true));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "fantasy" }));

        Assert.True((await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers").GetProperty("exempt").GetBoolean());
    }

    [Fact]
    public async Task Sell_Sold_EchoesTheAllowanceIncludingThisSell()
    {
        AllowanceChangesWhenCommitted(
            before: new TransferAllowance("7", 7, FreeAvailable: 1, Used: 0, Extra: 0, PointsHit: 0, Exempt: false),
            after: new TransferAllowance("7", 7, FreeAvailable: 1, Used: 1, Extra: 0, PointsHit: 0, Exempt: false),
            onCommit: commit => _factory.Sell
                .Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SellPlayerResult.Sold(View()))
                .Callback(commit));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Delete, "/api/users/me/squad/players/p-1?flavor=fantasy", token));

        var transfers = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers");
        Assert.Equal(1, transfers.GetProperty("used").GetInt32());
        Assert.Equal(0, transfers.GetProperty("extra").GetInt32());
    }

    [Fact]
    public async Task Buy_Committed_WithNoAllowanceAvailable_EchoesTransfersNull()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyPlayerResult.Committed(View()));
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetTransferAllowanceResult.NotAvailable.Instance);
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "fantasy" }));

        Assert.Equal(JsonValueKind.Null, (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers").ValueKind);
    }

    [Fact]
    public async Task Buy_Rejected_DoesNotComputeTheAllowance()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyPlayerResult.Rejected(new[] { new BuyRuleViolation("insufficient_budget", "x") }));
        var token = await TokenAsync();

        await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "fantasy" }));

        _factory.Allowance.VerifyNoOtherCalls();
    }

    private void AllowanceLookupThrows() =>
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));

    [Fact]
    public async Task Buy_Committed_WhenAllowanceLookupFails_StillReturns201WithTransfersNull()
    {
        _factory.Buy.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuyPlayerResult.Committed(View()));
        AllowanceLookupThrows();
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/users/me/squad/players", token, new { playerId = "p-1", flavor = "fantasy" }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers").ValueKind);
    }

    [Fact]
    public async Task Sell_Sold_WhenAllowanceLookupFails_StillReturns200WithTransfersNull()
    {
        _factory.Sell.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "p-1", It.IsAny<BuyPlayerContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SellPlayerResult.Sold(View()));
        AllowanceLookupThrows();
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Delete, "/api/users/me/squad/players/p-1?flavor=fantasy", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers").ValueKind);
    }
}
