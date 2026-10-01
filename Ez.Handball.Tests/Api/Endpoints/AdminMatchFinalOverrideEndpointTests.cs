using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class AdminMatchFinalOverrideEndpointTests : IClassFixture<AdminMatchFinalOverrideEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<ISetMatchFinalOverrideUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(ISetMatchFinalOverrideUseCase)));
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private const string Path = "/api/admin/matches/111453/final-override";

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public AdminMatchFinalOverrideEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    private string TokenFor(bool isAdmin) =>
        _factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(new UserEntity
        {
            RowKey = "u-1", Email = "a@b.is", DisplayName = "Jón", EmailVerified = true, IsAdmin = isAdmin
        });

    private HttpRequestMessage Authed(HttpMethod method, string token, string path = Path) =>
        new(method, path) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };

    [Fact]
    public async Task Put_WithoutToken_Returns401()
    {
        var response = await _client.PutAsync(Path, null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_NonAdminToken_Returns403()
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Put, TokenFor(isAdmin: false)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_SetsTheOverride_AsTheCallingAdmin()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync("111453", true, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetMatchFinalOverrideResult.Ok("9142", "111453", true));

        var response = await _client.SendAsync(Authed(HttpMethod.Put, TokenFor(isAdmin: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("9142", body.GetProperty("tournamentId").GetString());
        Assert.True(body.GetProperty("finalOverride").GetBoolean());
    }

    [Fact]
    public async Task Delete_ClearsTheOverride()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync("111453", false, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetMatchFinalOverrideResult.Ok("9142", "111453", false));

        var response = await _client.SendAsync(Authed(HttpMethod.Delete, TokenFor(isAdmin: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("finalOverride").GetBoolean());
    }

    [Fact]
    public async Task Put_UnknownMatch_Returns404()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync("999", true, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SetMatchFinalOverrideResult.MatchNotFound.Instance);

        var response = await _client.SendAsync(Authed(HttpMethod.Put, TokenFor(isAdmin: true), "/api/admin/matches/999/final-override"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("match_not_found", body.GetProperty("error").GetString());
    }
}
