using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Infrastructure;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ez.Handball.Tests.Api.Endpoints;

[Collection("Azurite")]
public class ShortlistEndpointTests : IClassFixture<ShortlistEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        static Factory()
        {
            // Program reads Storage:ConnectionString eagerly while the host builder is
            // assembled (to register the TableServiceClient), so the value must come from
            // a config source present before the host builds. The local (gitignored,
            // untracked) appsettings.Development.json points Storage:ConnectionString at a
            // real Azure account, and the factory's ConfigureAppConfiguration in-memory
            // override is layered too late to win that race. Environment variables are
            // added by WebApplication.CreateBuilder after the JSON files, so they win —
            // pinning the app to Azurite for these integration tests.
            Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");

            // Shortlist:MaxSize is likewise read eagerly at builder time (to register
            // ShortlistSettings), so the cap must also come from an environment variable
            // to win the CreateBuilder race over the default. 2 keeps the cap test cheap.
            Environment.SetEnvironmentVariable("Shortlist__MaxSize", "2");
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Jwt:SigningKey"] = "integration-test-signing-key-32-bytes-min!!",
                    ["Jwt:Issuer"] = "ez-handball",
                    ["Jwt:Audience"] = "ez-handball-web",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Shortlist:MaxSize"] = "2",
                    ["Auth:RateLimit:PermitLimit"] = "1000",
                    ["Auth:RateLimit:SensitivePermitLimit"] = "1000"
                });
            });
            return base.CreateHost(builder);
        }
    }

    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public ShortlistEndpointTests(Factory factory) => _client = factory.CreateClient();

    public async Task InitializeAsync()
    {
        var clubs = _tables.GetTableClient(Tables.Clubs);
        await clubs.CreateIfNotExistsAsync();
        await clubs.UpsertEntityAsync(new ClubEntity { PartitionKey = "club", RowKey = "385", Name = "Stjarnan" });

        var players = _tables.GetTableClient(Tables.Players);
        await players.CreateIfNotExistsAsync();
        foreach (var id in new[] { "p-1", "p-2", "p-3" })
        {
            await players.UpsertEntityAsync(new PlayerEntity
            {
                PartitionKey = "385-karlar", RowKey = id, Name = "Player " + id,
                Position = "VS", Gender = "karlar", ClubId = "385", ClubName = "Stjarnan"
            });
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var t in new[]
                 {
                     Tables.Users, Tables.UserEmailIndex, Tables.RefreshTokens,
                     Tables.EmailTokens, Tables.Clubs, Tables.Players, Tables.Shortlists
                 })
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

    private HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Shortlist_WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/users/me/shortlist");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Add_Get_Remove_RoundTrip()
    {
        var token = await RegisterAndGetTokenAsync();

        var add = await _client.SendAsync(Authed(HttpMethod.Put, "/api/users/me/shortlist/p-1", token));
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);

        // idempotent re-add
        var add2 = await _client.SendAsync(Authed(HttpMethod.Put, "/api/users/me/shortlist/p-1", token));
        Assert.Equal(HttpStatusCode.NoContent, add2.StatusCode);

        var get = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me/shortlist", token));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("count").GetInt32());
        Assert.Equal(2, body.GetProperty("max").GetInt32());
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("p-1", items[0].GetProperty("playerId").GetString());
        Assert.Equal("VS", items[0].GetProperty("position").GetString());

        var del = await _client.SendAsync(Authed(HttpMethod.Delete, "/api/users/me/shortlist/p-1", token));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var getAfter = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me/shortlist", token));
        var bodyAfter = await getAfter.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, bodyAfter.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Add_UnknownPlayer_Returns404()
    {
        var token = await RegisterAndGetTokenAsync();
        var resp = await _client.SendAsync(Authed(HttpMethod.Put, "/api/users/me/shortlist/ghost", token));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Add_BlankPlayerId_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();
        // "%20" decodes to a whitespace playerId, which the route binds but the boundary rejects.
        var resp = await _client.SendAsync(Authed(HttpMethod.Put, "/api/users/me/shortlist/%20", token));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_player_id", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Add_OverCap_Returns409ShortlistFull()
    {
        var token = await RegisterAndGetTokenAsync();
        var add1 = await _client.SendAsync(Authed(HttpMethod.Put, "/api/users/me/shortlist/p-1", token));
        var add2 = await _client.SendAsync(Authed(HttpMethod.Put, "/api/users/me/shortlist/p-2", token));
        Assert.Equal(HttpStatusCode.NoContent, add1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, add2.StatusCode);

        var resp = await _client.SendAsync(Authed(HttpMethod.Put, "/api/users/me/shortlist/p-3", token));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("shortlist_full", body.GetProperty("error").GetString());
        Assert.Equal(2, body.GetProperty("details").GetProperty("max").GetInt32());
    }
}
