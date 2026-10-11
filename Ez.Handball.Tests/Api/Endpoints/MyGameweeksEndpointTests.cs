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
public class MyGameweeksEndpointTests : IClassFixture<MyGameweeksEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetMyGameweekScoresUseCase> Uc { get; } = new();

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
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetMyGameweekScoresUseCase)));
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public MyGameweeksEndpointTests(Factory factory)
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
        foreach (var t in new[] { Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens, Tables.EmailTokens, Tables.Clubs, Tables.GameTeams, Tables.GameBudgets, Tables.GameTeamNameIndex })
            try { await _tables.GetTableClient(t).DeleteAsync(); } catch { /* not created */ }
    }

    private async Task<string> TokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = $"u{Guid.NewGuid():N}@test.is", password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385", teamName = $"Test {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<JsonElement> GetAsync()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/users/me/gameweeks");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private void ScoresAre(params GameweekScore[] scores) =>
        _factory.Uc.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGameweekScores(scores.Sum(s => s.Points), scores, new Dictionary<string, Player>()));

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/users/me/gameweeks");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task RoundWithAHit_ReportsNetPointsHitAndGross()
    {
        ScoresAre(new GameweekScore("u:fantasy", "7", 5, null, [], TransferHit: 4));

        var round = (await GetAsync()).GetProperty("gameweeks")[0];

        Assert.Equal(5, round.GetProperty("points").GetDouble());
        Assert.Equal(4, round.GetProperty("transferHit").GetInt32());
        Assert.Equal(9, round.GetProperty("grossPoints").GetDouble());
    }

    [Fact]
    public async Task RoundWithoutAHit_ReportsZeroHit_AndGrossEqualToPoints()
    {
        ScoresAre(new GameweekScore("u:fantasy", "6", 12, null, []));

        var round = (await GetAsync()).GetProperty("gameweeks")[0];

        Assert.Equal(0, round.GetProperty("transferHit").GetInt32());
        Assert.Equal(12, round.GetProperty("grossPoints").GetDouble());
    }

    [Fact]
    public async Task RunningTotal_IsTheSumOfNetPoints()
    {
        ScoresAre(
            new GameweekScore("u:fantasy", "6", 12, null, []),
            new GameweekScore("u:fantasy", "7", 5, null, [], TransferHit: 4));

        Assert.Equal(17, (await GetAsync()).GetProperty("runningTotal").GetDouble());
    }
}
