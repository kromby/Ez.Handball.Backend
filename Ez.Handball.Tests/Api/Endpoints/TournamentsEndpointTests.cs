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

public class TournamentsEndpointTests : IClassFixture<TournamentsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetTournamentsUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetTournamentsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public TournamentsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_WithSeason_ReturnsTournaments_With200AndExpectedShape()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync("2025-26", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tournament>
            {
                new("8444", "Olís deild karla", "karlar")
            });

        var response = await _client.GetAsync("/api/tournaments?season=2025-26");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        var first = body[0];
        Assert.Equal("8444", first.GetProperty("tournamentId").GetString());
        Assert.Equal("Olís deild karla", first.GetProperty("name").GetString());
        Assert.Equal("karlar", first.GetProperty("gender").GetString());
    }

    [Fact]
    public async Task Get_WithoutSeason_PassesNullToUseCase()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tournament> { new("8444", "Olís deild karla", "karlar") });

        var response = await _client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Uc.Verify(s => s.ExecuteAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_EmptyData_Returns200WithEmptyArray()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tournament>());

        var response = await _client.GetAsync("/api/tournaments?season=1999-00");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }
}
