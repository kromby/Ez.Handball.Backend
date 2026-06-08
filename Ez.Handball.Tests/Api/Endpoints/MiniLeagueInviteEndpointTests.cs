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
public class MiniLeagueInviteEndpointTests : IClassFixture<MiniLeagueInviteEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGenerateInviteUseCase> Generate { get; } = new();
        public Mock<IGetInviteUseCase> Get { get; } = new();
        public Mock<IPreviewInviteUseCase> Preview { get; } = new();
        public Mock<IJoinMiniLeagueUseCase> Join { get; } = new();

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
                services.Remove(services.Single(d => d.ServiceType == typeof(IGenerateInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IPreviewInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IJoinMiniLeagueUseCase)));
                services.AddSingleton(Generate.Object);
                services.AddSingleton(Get.Object);
                services.AddSingleton(Preview.Object);
                services.AddSingleton(Join.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    public MiniLeagueInviteEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Generate.Reset();
        _factory.Get.Reset();
        _factory.Preview.Reset();
        _factory.Join.Reset();
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
    public async Task Generate_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/mini-leagues/lg-1/invite", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Join_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/mini-leagues/join", new { token = "tok-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Generate_HappyPath_Returns201WithTokenAndExpiry()
    {
        _factory.Generate.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "lg-1", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateInviteResult.Generated("tok-abc", T0.AddDays(7)));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-1/invite", token, new { expiresInDays = 7 }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tok-abc", body.GetProperty("token").GetString());
        Assert.Equal(T0.AddDays(7), body.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Generate_NotMember_Returns403()
    {
        _factory.Generate.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateInviteResult.NotMember());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-1/invite", token, new { }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_member", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Generate_LeagueNotFound_Returns404()
    {
        _factory.Generate.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateInviteResult.LeagueNotFound());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-x/invite", token, new { }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("league_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Generate_InvalidExpiry_Returns400()
    {
        _factory.Generate.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateInviteResult.InvalidExpiry());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-1/invite", token, new { expiresInDays = 0 }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_expiry", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetInvite_Found_Returns200()
    {
        _factory.Get.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetInviteResult.Found("tok-abc", null));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Get, "/api/mini-leagues/lg-1/invite", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tok-abc", body.GetProperty("token").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task GetInvite_NoInvite_Returns404()
    {
        _factory.Get.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetInviteResult.NoInvite());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Get, "/api/mini-leagues/lg-1/invite", token));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no_invite", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Preview_Found_Returns200WithSummary()
    {
        _factory.Preview.Setup(u => u.ExecuteAsync("tok-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreviewInviteResult.Found("lg-1", "Office League", "2025-26", 3));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Get, "/api/mini-leagues/invite/tok-abc", token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("lg-1", body.GetProperty("leagueId").GetString());
        Assert.Equal("Office League", body.GetProperty("name").GetString());
        Assert.Equal(3, body.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Preview_Invalid_Returns404()
    {
        _factory.Preview.Setup(u => u.ExecuteAsync("bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreviewInviteResult.InvalidInvite());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Get, "/api/mini-leagues/invite/bad", token));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_invite", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Preview_Expired_Returns410()
    {
        _factory.Preview.Setup(u => u.ExecuteAsync("old", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreviewInviteResult.InviteExpired());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Get, "/api/mini-leagues/invite/old", token));

        Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invite_expired", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Join_BlankToken_Returns400()
    {
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/join", token, new { token = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_token", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Join_HappyPath_Returns200WithMemberRole()
    {
        _factory.Join.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uid, string tok, CancellationToken _) =>
                new JoinMiniLeagueResult.Joined(new MiniLeagueView(
                    new MiniLeague("lg-1", "Office League", "2025-26", "creator-x", T0),
                    new[]
                    {
                        new MiniLeagueMember("creator-x", MiniLeagueRoles.Creator, T0),
                        new MiniLeagueMember(uid, MiniLeagueRoles.Member, T0)
                    })));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/join", token, new { token = "tok-1" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("lg-1", body.GetProperty("id").GetString());
        Assert.Equal(2, body.GetProperty("memberCount").GetInt32());
        Assert.Equal("member", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Join_AlreadyMember_Returns200()
    {
        _factory.Join.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "tok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uid, string tok, CancellationToken _) =>
                new JoinMiniLeagueResult.AlreadyMember(new MiniLeagueView(
                    new MiniLeague("lg-1", "Office League", "2025-26", "creator-x", T0),
                    new[] { new MiniLeagueMember(uid, MiniLeagueRoles.Member, T0) })));
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/join", token, new { token = "tok-1" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("member", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Join_InvalidInvite_Returns404()
    {
        _factory.Join.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JoinMiniLeagueResult.InvalidInvite());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/join", token, new { token = "bad" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_invite", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Join_Expired_Returns410()
    {
        _factory.Join.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "old", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JoinMiniLeagueResult.InviteExpired());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/join", token, new { token = "old" }));

        Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invite_expired", body.GetProperty("error").GetString());
    }
}
