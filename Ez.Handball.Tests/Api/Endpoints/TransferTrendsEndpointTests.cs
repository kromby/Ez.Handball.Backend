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

[Collection("Azurite")]
public class TransferTrendsEndpointTests : IClassFixture<TransferTrendsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetTransferTrendsUseCase> Trends { get; } = new();

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
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetTransferTrendsUseCase)));
                services.AddSingleton(Trends.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public TransferTrendsEndpointTests(Factory factory)
    {
        _factory = factory; _factory.Trends.Reset();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Trends_Ok_ReturnsSignedAndDroppedShape()
    {
        _factory.Trends.Setup(u => u.ExecuteAsync(GameFlavor.Fantasy, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferTrendsResult.Ok(new TransferTrends(
                new[] { new TransferTrendEntry("p-1", "Aron", "Stjarnan", 5) },
                new[] { new TransferTrendEntry("p-9", "Jon", "Valur", 3) })));

        var resp = await _client.GetAsync("/api/transfers/trends?flavor=fantasy&window=7d");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var signed = body.GetProperty("mostSigned")[0];
        Assert.Equal("p-1", signed.GetProperty("playerId").GetString());
        Assert.Equal("Aron", signed.GetProperty("name").GetString());
        Assert.Equal("Stjarnan", signed.GetProperty("clubName").GetString());
        Assert.Equal(5, signed.GetProperty("count").GetInt32());
        Assert.Equal("p-9", body.GetProperty("mostDropped")[0].GetProperty("playerId").GetString());
    }

    [Fact]
    public async Task Trends_InvalidWindow_Returns400()
    {
        _factory.Trends.Setup(u => u.ExecuteAsync(GameFlavor.Fantasy, "5d", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransferTrendsResult.InvalidWindow.Instance);

        var resp = await _client.GetAsync("/api/transfers/trends?window=5d");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_window", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Trends_InvalidFlavor_Returns400()
    {
        var resp = await _client.GetAsync("/api/transfers/trends?flavor=manager");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_flavor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Trends_NoAuthRequired()
    {
        _factory.Trends.Setup(u => u.ExecuteAsync(GameFlavor.Fantasy, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransferTrendsResult.Ok(new TransferTrends(
                Array.Empty<TransferTrendEntry>(), Array.Empty<TransferTrendEntry>())));

        var resp = await _client.GetAsync("/api/transfers/trends");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
