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

public class SeasonsEndpointTests : IClassFixture<SeasonsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetSeasonsUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetSeasonsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public SeasonsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsSeasons_With200AndExpectedShape()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Season>
            {
                new("2025-26", true),
                new("2024-25", false)
            });

        var response = await _client.GetAsync("/api/seasons");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());

        var first = body[0];
        Assert.Equal("2025-26", first.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.True, first.GetProperty("isCurrent").ValueKind);
        Assert.Equal(JsonValueKind.False, body[1].GetProperty("isCurrent").ValueKind);
    }

    [Fact]
    public async Task Get_EmptyData_Returns200WithEmptyArray()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Season>());

        var response = await _client.GetAsync("/api/seasons");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }
}
