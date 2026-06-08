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
public class MiniLeagueEndpointTests : IClassFixture<MiniLeagueEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<ICreateMiniLeagueUseCase> Create { get; } = new();
        public Mock<IGetMiniLeagueUseCase> Get { get; } = new();

        static Factory()
        {
            Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");
            Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-test-signing-key-32-bytes-min!!");
        }

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
                services.Remove(services.Single(d => d.ServiceType == typeof(ICreateMiniLeagueUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetMiniLeagueUseCase)));
                services.AddSingleton(Create.Object);
                services.AddSingleton(Get.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    public MiniLeagueEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Create.Reset();
        _factory.Get.Reset();
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
        foreach (var t in new[] { Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens, Tables.EmailTokens, Tables.Clubs, Tables.GameTeams, Tables.GameTeamNameIndex, Tables.GameBudgets })
            try { await _tables.GetTableClient(t).DeleteAsync(); } catch { /* not created */ }
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

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/mini-leagues", new { name = "Office League" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/mini-leagues/lg-1");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Create_HappyPath_Returns201WithCreatorRole()
    {
        // Echo the caller's userId back into the view so the endpoint resolves role == "creator".
        _factory.Create.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "Office League", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uid, string name, CancellationToken _) =>
                new CreateMiniLeagueResult.Created(new MiniLeagueView(
                    new MiniLeague("lg-1", name, "2025-26", uid, T0),
                    new[] { new MiniLeagueMember(uid, MiniLeagueRoles.Creator, T0) })));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues", token, new { name = "Office League" }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("lg-1", body.GetProperty("id").GetString());
        Assert.Equal("Office League", body.GetProperty("name").GetString());
        Assert.Equal("2025-26", body.GetProperty("season").GetString());
        Assert.Equal(1, body.GetProperty("memberCount").GetInt32());
        Assert.Equal("creator", body.GetProperty("role").GetString());
        var member = body.GetProperty("members")[0];
        Assert.Equal("creator", member.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Create_BlankName_Returns400InvalidName()
    {
        _factory.Create.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateMiniLeagueResult.ValidationError("name"));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues", token, new { name = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_name", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_NoCurrentSeason_Returns409()
    {
        _factory.Create.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateMiniLeagueResult.NoCurrentSeason());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues", token, new { name = "Office League" }));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no_current_season", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_Found_Returns200_RoleNullForNonMember()
    {
        // Members do not include the caller, so the top-level role resolves to null.
        _factory.Get.Setup(u => u.ExecuteAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetMiniLeagueResult.Found(new MiniLeagueView(
                new MiniLeague("lg-1", "Office League", "2025-26", "someone-else", T0),
                new[] { new MiniLeagueMember("someone-else", MiniLeagueRoles.Creator, T0) })));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Get, "/api/mini-leagues/lg-1", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Office League", body.GetProperty("name").GetString());
        Assert.Equal(1, body.GetProperty("memberCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("role").ValueKind);
    }

    [Fact]
    public async Task Get_NotFound_Returns404()
    {
        _factory.Get.Setup(u => u.ExecuteAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetMiniLeagueResult.NotFound());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Get, "/api/mini-leagues/missing", token));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("league_not_found", body.GetProperty("error").GetString());
    }
}
