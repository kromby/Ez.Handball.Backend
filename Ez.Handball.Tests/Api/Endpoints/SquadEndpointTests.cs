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
public class SquadEndpointTests : IClassFixture<SquadEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetSquadUseCase> Uc { get; } = new();
        public Mock<IGetTransferAllowanceUseCase> Allowance { get; } = new();

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
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetSquadUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetTransferAllowanceUseCase)));
                services.AddSingleton(Allowance.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public SquadEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _factory.Allowance.Reset();
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

    private static SquadView SampleView() => new(
        new[]
        {
            new SquadPlayer("p-1", "Aron", "385", "Stjarnan", "VS", "karlar",
                new PlayerPrice(50_000_000, "ISK"), new PlayerPrice(42_000_000, "ISK"), Rating: 0.0,
                PositionSecondary: "ST", AssignedPosition: "ST", PlayAs: "ST")
        },
        BudgetUsed: new PlayerPrice(42_000_000, "ISK"),
        RemainingBudget: new PlayerPrice(8_000_000, "ISK"),
        SquadValue: new PlayerPrice(50_000_000, "ISK"));

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/users/me/squad");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Found_Returns200WithExpectedShape()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(SampleView()));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/users/me/squad?flavor=fantasy", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fantasy", body.GetProperty("flavor").GetString());
        var player = body.GetProperty("players")[0];
        Assert.Equal("p-1", player.GetProperty("playerId").GetString());
        Assert.Equal("ST", player.GetProperty("positionSecondary").GetString());
        Assert.Equal("ST", player.GetProperty("assignedPosition").GetString());
        Assert.Equal("ST", player.GetProperty("playAs").GetString());
        Assert.Equal(50_000_000, player.GetProperty("price").GetProperty("amount").GetDouble());
        Assert.Equal(42_000_000, player.GetProperty("pricePaid").GetProperty("amount").GetDouble());
        Assert.Equal(42_000_000, body.GetProperty("budgetUsed").GetProperty("amount").GetDouble());
        Assert.Equal(8_000_000, body.GetProperty("remainingBudget").GetProperty("amount").GetDouble());
        Assert.Equal(50_000_000, body.GetProperty("squadValue").GetProperty("amount").GetDouble());
        Assert.Equal("ISK", body.GetProperty("squadValue").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task ForwardsScopeQueryParamsToUseCase()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), "2024", "t-9", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(SampleView()));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(
            Authed("/api/users/me/squad?flavor=fantasy&season=2024&tournamentId=t-9&ruleSetVersion=2", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(), "2024", "t-9", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidFlavor_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/users/me/squad?flavor=manager", token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RuleSetNotFound_Returns400()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), null, null, 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetSquadResult.RuleSetNotFound.Instance);
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/users/me/squad?ruleSetVersion=9", token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }

    private void SquadFound() => _factory.Uc.Setup(s => s.ExecuteAsync(
            It.IsAny<string>(), null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GetSquadResult.Found(SampleView()));

    [Fact]
    public async Task Found_IncludesTheCurrentRoundsTransferAllowance()
    {
        SquadFound();
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTransferAllowanceResult.Found(
                new TransferAllowance("7", 7, FreeAvailable: 2, Used: 3, Extra: 1, PointsHit: 4, Exempt: false)));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/users/me/squad?flavor=fantasy", token));

        var transfers = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers");
        Assert.Equal("7", transfers.GetProperty("roundLabel").GetString());
        Assert.Equal(7, transfers.GetProperty("roundNumber").GetInt32());
        Assert.Equal(2, transfers.GetProperty("freeAvailable").GetInt32());
        Assert.Equal(3, transfers.GetProperty("used").GetInt32());
        Assert.Equal(1, transfers.GetProperty("extra").GetInt32());
        Assert.Equal(4, transfers.GetProperty("pointsHit").GetInt32());
        Assert.False(transfers.GetProperty("exempt").GetBoolean());
    }

    [Fact]
    public async Task Found_WhileBuildingTheSquad_ReportsExempt()
    {
        SquadFound();
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTransferAllowanceResult.Found(
                new TransferAllowance("1", 1, FreeAvailable: 1, Used: 7, Extra: 0, PointsHit: 0, Exempt: true)));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/users/me/squad?flavor=fantasy", token));

        var transfers = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers");
        Assert.True(transfers.GetProperty("exempt").GetBoolean());
        Assert.Equal(0, transfers.GetProperty("pointsHit").GetInt32());
    }

    [Fact]
    public async Task Found_WithNoAllowanceAvailable_ReturnsTransfersNull()
    {
        SquadFound();
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetTransferAllowanceResult.NotAvailable.Instance);
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/users/me/squad?flavor=fantasy", token));

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("transfers").ValueKind);
    }

    [Fact]
    public async Task Found_WhenAllowanceLookupFails_StillReturns200WithTransfersNull()
    {
        SquadFound();
        _factory.Allowance.Setup(a => a.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed("/api/users/me/squad?flavor=fantasy", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfers").ValueKind);
    }
}
