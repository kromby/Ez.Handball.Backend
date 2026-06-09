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
public class LineupEndpointTests : IClassFixture<LineupEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetLineupUseCase> Get { get; } = new();
        public Mock<ISetLineupUseCase> Set { get; } = new();

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
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetLineupUseCase)));
                services.AddSingleton(Get.Object);
                services.Remove(services.Single(d => d.ServiceType == typeof(ISetLineupUseCase)));
                services.AddSingleton(Set.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public LineupEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Get.Reset();
        _factory.Set.Reset();
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

    private HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static LineupView SampleView() => new(
        new[]
        {
            new LineupPlayer("p0", "Aron", "Stjarnan", "GK", new PlayerPrice(10_000_000, "ISK"), LineupRole.Captain, null),
            new LineupPlayer("p7", "Bjarki", "Stjarnan", "CB", new PlayerPrice(8_000_000, "ISK"), LineupRole.Bench, 0),
        },
        CaptainMultiplier: 2, IsValid: true, Violations: Array.Empty<LineupViolation>());

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/users/me/lineup");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_InvalidFlavor_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();
        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me/lineup?flavor=manager", token));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_Found_Returns200WithSplitShape()
    {
        _factory.Get.Setup(s => s.ExecuteAsync(It.IsAny<string>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetLineupResult.Found(SampleView()));
        var token = await RegisterAndGetTokenAsync();

        var resp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me/lineup", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p0", body.GetProperty("captainId").GetString());
        Assert.Equal(1, body.GetProperty("starters").GetArrayLength());
        Assert.Equal(1, body.GetProperty("bench").GetArrayLength());
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.Equal(2, body.GetProperty("captainMultiplier").GetDouble());
    }

    [Fact]
    public async Task Put_MalformedBody_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();
        var req = Authed(HttpMethod.Put, "/api/users/me/lineup", token);
        // starters present but bench omitted → malformed
        req.Content = JsonContent.Create(new { flavor = "fantasy", starters = new[] { new { playerId = "p0", role = "Captain" } } });

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("malformed_body", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Put_Rejected_Returns422WithViolations()
    {
        _factory.Set.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<Lineup>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetLineupResult.Rejected(new[] { new LineupViolation("wrong_starter_count", "x") }));
        var token = await RegisterAndGetTokenAsync();
        var req = Authed(HttpMethod.Put, "/api/users/me/lineup", token);
        req.Content = JsonContent.Create(new
        {
            flavor = "fantasy",
            starters = new[] { new { playerId = "p0", role = "Captain" } },
            bench = Array.Empty<string>()
        });

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("wrong_starter_count", body.GetProperty("violations")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Put_Committed_Returns200()
    {
        _factory.Set.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<Lineup>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetLineupResult.Committed(SampleView()));
        var token = await RegisterAndGetTokenAsync();
        var req = Authed(HttpMethod.Put, "/api/users/me/lineup", token);
        req.Content = JsonContent.Create(new
        {
            flavor = "fantasy",
            starters = new[] { new { playerId = "p0", role = "Captain" } },
            bench = new[] { "p7" }
        });

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("p0", body.GetProperty("captainId").GetString());
    }
}
