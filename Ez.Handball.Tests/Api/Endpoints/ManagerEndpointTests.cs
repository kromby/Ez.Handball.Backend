using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Infrastructure;
using Ez.Handball.Shared.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

[Collection("Azurite")]
public class ManagerEndpointTests : IClassFixture<ManagerEndpointTests.Factory>, IAsyncLifetime
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetManagerUseCase> Get { get; } = new();
        public Mock<IRenameTeamUseCase> Rename { get; } = new();

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
                var g = services.Single(d => d.ServiceType == typeof(IGetManagerUseCase));
                services.Remove(g);
                services.AddSingleton(Get.Object);
                var r = services.Single(d => d.ServiceType == typeof(IRenameTeamUseCase));
                services.Remove(r);
                services.AddSingleton(Rename.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;
    private readonly TableServiceClient _tables = new("UseDevelopmentStorage=true");

    public ManagerEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Get.Reset();
        _factory.Rename.Reset();
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

    private static string NewEmail() => $"u{Guid.NewGuid():N}@test.is";

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "hunter2hunter2", displayName = "Jón", language = "is", favoriteClubId = "385", teamName = $"Test {Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static ManagerView SampleView() =>
        new("Dream Team", "385", "#1E88E5", new OnboardingView(false, 9, 15));

    [Fact]
    public async Task GetWithoutToken_Returns401()
    {
        var resp = await _client.GetAsync("/api/users/me/manager");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Found_Returns200WithShape()
    {
        _factory.Get.Setup(s => s.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetManagerResult.Found(SampleView()));
        var token = await RegisterAndGetTokenAsync();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/users/me/manager");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fantasy", body.GetProperty("flavor").GetString());
        Assert.Equal("Dream Team", body.GetProperty("teamName").GetString());
        Assert.Equal("385", body.GetProperty("favoriteClubId").GetString());
        Assert.Equal("#1E88E5", body.GetProperty("color").GetString());
        var onb = body.GetProperty("onboarding");
        Assert.False(onb.GetProperty("squadComplete").GetBoolean());
        Assert.Equal(9, onb.GetProperty("playersOwned").GetInt32());
        Assert.Equal(15, onb.GetProperty("squadSize").GetInt32());
    }

    [Fact]
    public async Task Get_InvalidFlavor_Returns400()
    {
        var token = await RegisterAndGetTokenAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/users/me/manager?flavor=manager");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_Success_Returns200()
    {
        _factory.Rename.Setup(s => s.ExecuteAsync(It.IsAny<string>(), "New Name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RenameTeamResult.Success(new ManagerView("New Name", "385", "#1E88E5", new OnboardingView(false, 9, 15))));
        var token = await RegisterAndGetTokenAsync();

        var req = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me/manager")
        { Content = JsonContent.Create(new { teamName = "New Name" }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New Name", body.GetProperty("teamName").GetString());
    }

    [Fact]
    public async Task Patch_Taken_Returns409()
    {
        _factory.Rename.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RenameTeamResult.TeamNameTaken());
        var token = await RegisterAndGetTokenAsync();

        var req = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me/manager")
        { Content = JsonContent.Create(new { teamName = "Taken" }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_Blocked_Returns400ValidationError()
    {
        _factory.Rename.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RenameTeamResult.ValidationError("teamName"));
        var token = await RegisterAndGetTokenAsync();

        var req = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me/manager")
        { Content = JsonContent.Create(new { teamName = "admin" }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", body.GetProperty("error").GetString());
        Assert.Equal("teamName", body.GetProperty("details").GetProperty("field").GetString());
    }
}
