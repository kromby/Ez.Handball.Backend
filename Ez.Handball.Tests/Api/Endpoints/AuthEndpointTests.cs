using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ez.Handball.Tests.Api.Endpoints;

[Collection("Azurite")]
public class AuthEndpointTests : IClassFixture<AuthEndpointTests.Factory>, IAsyncLifetime
{
    public sealed class StubEmailSender : IEmailSender
    {
        public string? LastVerificationToken;
        public string? LastResetToken;

        public Task SendVerificationEmailAsync(string email, string link, string token, CancellationToken ct)
        {
            LastVerificationToken = token;
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string email, string link, string token, CancellationToken ct)
        {
            LastResetToken = token;
            return Task.CompletedTask;
        }
    }

    public class Factory : WebApplicationFactory<Program>
    {
        public StubEmailSender Email { get; } = new();

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
                    ["Jwt:AccessTokenMinutes"] = "15"
                });
            });
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IEmailSender));
                services.Remove(descriptor);
                services.AddSingleton<IEmailSender>(Email);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public AuthEndpointTests(Factory factory)
    {
        _factory = factory;
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

    private async Task<JsonElement> RegisterAsync(string email, string password = "hunter2hunter2", string club = "385")
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password, displayName = "Jón", language = "is", favoriteClubId = club, teamName = $"Test {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string accessToken)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return req;
    }

    [Fact]
    public async Task Register_Response_HasTokenShape_AndNoPasswordHash()
    {
        var body = await RegisterAsync(NewEmail());

        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("refreshToken").GetString()));
        Assert.Equal(JsonValueKind.Number, body.GetProperty("expiresIn").ValueKind);

        var user = body.GetProperty("user");
        Assert.False(user.TryGetProperty("passwordHash", out _));
        Assert.False(user.GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task HappyPath_Register_Me_Refresh_Logout()
    {
        var email = NewEmail();
        var reg = await RegisterAsync(email);
        var access = reg.GetProperty("accessToken").GetString()!;
        var refresh = reg.GetProperty("refreshToken").GetString()!;

        var meResp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me", access));
        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
        var me = await meResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, me.GetProperty("email").GetString());

        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
        var refreshed = await refreshResp.Content.ReadFromJsonAsync<JsonElement>();
        var newRefresh = refreshed.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(refresh, newRefresh);

        var reuseResp = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResp.StatusCode);

        var logoutResp = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = newRefresh });
        Assert.Equal(HttpStatusCode.NoContent, logoutResp.StatusCode);
    }

    [Fact]
    public async Task Verification_Flow_MarksEmailVerified()
    {
        var email = NewEmail();
        var reg = await RegisterAsync(email);
        var access = reg.GetProperty("accessToken").GetString()!;

        var token = _factory.Email.LastVerificationToken;
        Assert.False(string.IsNullOrEmpty(token));

        var verifyResp = await _client.PostAsJsonAsync("/api/auth/verify", new { token });
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);

        var meResp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users/me", access));
        var me = await meResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(me.GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task PasswordReset_Flow_RevokesOldSessions_AndAllowsNewLogin()
    {
        var email = NewEmail();
        var reg = await RegisterAsync(email);
        var oldRefresh = reg.GetProperty("refreshToken").GetString()!;

        var reqResp = await _client.PostAsJsonAsync("/api/auth/password/reset-request", new { email });
        Assert.Equal(HttpStatusCode.Accepted, reqResp.StatusCode);

        var resetToken = _factory.Email.LastResetToken;
        Assert.False(string.IsNullOrEmpty(resetToken));

        var resetResp = await _client.PostAsJsonAsync("/api/auth/password/reset",
            new { token = resetToken, newPassword = "brandnewpass1" });
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

        var oldRefreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshResp.StatusCode);

        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "brandnewpass1" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
    }

    [Fact]
    public async Task DuplicateEmail_Returns409()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385", teamName = $"Test {Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("email_taken", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvalidClub_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "no-such-club", teamName = $"Test {Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_club", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task BadCredentials_Returns401InvalidCredentials()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrongpassword1" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_credentials", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ProtectedRoute_WithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
