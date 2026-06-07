using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class SquadConstraintsEndpointTests : IClassFixture<SquadConstraintsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetSquadConstraintsUseCase> Uc { get; } = new();

        static Factory()
        {
            Environment.SetEnvironmentVariable("Storage__ConnectionString", "UseDevelopmentStorage=true");
            Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-test-signing-key-32-bytes-min!!");
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
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetSquadConstraintsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public SquadConstraintsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = factory.CreateClient();
    }

    private static SquadConstraints Sample(int version) => new(
        version,
        MaxSquadSize: 15,
        PositionLimits: new Dictionary<string, int> { ["GK"] = 2, ["P"] = 2 },
        StartingCap: 100_000_000,
        Currency: "ISK");

    [Fact]
    public async Task Default_Returns200WithExpectedShape()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync((int?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadConstraintsResult.Found(Sample(1)));

        var resp = await _client.GetAsync("/api/squad/constraints?flavor=fantasy");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("ruleSetVersion").GetInt32());
        Assert.Equal(15, body.GetProperty("maxSquadSize").GetInt32());
        Assert.Equal(100_000_000, body.GetProperty("startingCap").GetProperty("amount").GetDouble());
        Assert.Equal("ISK", body.GetProperty("startingCap").GetProperty("currency").GetString());
        Assert.Equal(2, body.GetProperty("posLimits").GetProperty("GK").GetInt32());
    }

    [Fact]
    public async Task ExplicitVersion_ForwardedToUseCase()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadConstraintsResult.Found(Sample(2)));

        var resp = await _client.GetAsync("/api/squad/constraints?ruleSetVersion=2");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidFlavor_Returns400()
    {
        var resp = await _client.GetAsync("/api/squad/constraints?flavor=manager");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RuleSetNotFound_Returns400InvalidRuleSet()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetSquadConstraintsResult.RuleSetNotFound.Instance);

        var resp = await _client.GetAsync("/api/squad/constraints?ruleSetVersion=99");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_rule_set", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NoTokenAndNoFlavor_Returns200_EndpointIsPublic()
    {
        _factory.Uc.Setup(s => s.ExecuteAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadConstraintsResult.Found(Sample(1)));

        var resp = await _client.GetAsync("/api/squad/constraints");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
