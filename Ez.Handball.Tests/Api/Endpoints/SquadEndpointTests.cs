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
        foreach (var t in new[] { Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens, Tables.EmailTokens, Tables.Clubs })
        {
            try { await _tables.GetTableClient(t).DeleteAsync(); } catch { /* not created */ }
        }
    }

    private static string NewEmail() => $"u{Guid.NewGuid():N}@test.is";

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385" });
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
                new PlayerCost(50_000_000, "ISK"), new PlayerCost(42_000_000, "ISK"))
        },
        BudgetUsed: new PlayerCost(42_000_000, "ISK"),
        RemainingBudget: new PlayerCost(8_000_000, "ISK"),
        SquadValue: new PlayerCost(50_000_000, "ISK"));

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
}
