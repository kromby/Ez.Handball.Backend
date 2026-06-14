using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Ez.Handball.Tests.Api.Endpoints;

public class ManagerStandingsEndpointTests : IClassFixture<ManagerStandingsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetManagerStandingsUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetManagerStandingsUseCase)));
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ManagerStandingsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = factory.CreateClient();
    }

    private static ManagerStandings Empty(int offset = 0, int limit = 50) =>
        new(0, offset, limit, null, Array.Empty<ManagerStanding>());

    [Fact]
    public async Task Get_NoParams_DefaultsToOffset0Limit50()
    {
        var capturedOffset = -1;
        var capturedLimit = -1;
        _factory.Uc.Setup(u => u.ExecuteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((o, l, _) => { capturedOffset = o; capturedLimit = l; })
            .ReturnsAsync(Empty());

        var resp = await _client.GetAsync("/api/managers/leaderboard");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(0, capturedOffset);
        Assert.Equal(50, capturedLimit);
    }

    [Fact]
    public async Task Get_NegativeOffset_Returns400InvalidPagination()
    {
        var resp = await _client.GetAsync("/api/managers/leaderboard?offset=-1");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_pagination", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Get_LimitOver200_Returns400InvalidPagination()
    {
        var resp = await _client.GetAsync("/api/managers/leaderboard?limit=201");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsStandingsBody()
    {
        _factory.Uc.Setup(u => u.ExecuteAsync(0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManagerStandings(1, 0, 50, "2", new[]
            {
                new ManagerStanding(1, 2, 1, "a:fantasy", "Alpha", "#abcdef", 95, 40),
            }));

        var resp = await _client.GetAsync("/api/managers/leaderboard");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal("2", body.GetProperty("latestRoundLabel").GetString());
        var entry = body.GetProperty("entries")[0];
        Assert.Equal("Alpha", entry.GetProperty("teamName").GetString());
        Assert.Equal(1, entry.GetProperty("rankDelta").GetInt32());
    }
}
