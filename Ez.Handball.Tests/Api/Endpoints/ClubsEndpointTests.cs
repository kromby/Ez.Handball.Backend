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

public class ClubsEndpointTests : IClassFixture<ClubsEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGetClubsUseCase> Uc { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IGetClubsUseCase));
                services.Remove(descriptor);
                services.AddSingleton(Uc.Object);
            });
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public ClubsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Uc.Reset();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsClubs_With200AndExpectedShape()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Club>
            {
                new("385", "Afturelding", "https://logo/aft.png"),
                new("412", "Fram", null)
            });

        var response = await _client.GetAsync("/api/clubs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());

        var first = body[0];
        Assert.Equal("385", first.GetProperty("clubId").GetString());
        Assert.Equal("Afturelding", first.GetProperty("name").GetString());
        Assert.Equal("https://logo/aft.png", first.GetProperty("logoUrl").GetString());

        Assert.Equal(JsonValueKind.Null, body[1].GetProperty("logoUrl").ValueKind);
    }

    [Fact]
    public async Task Get_EmptyData_Returns200WithEmptyArray()
    {
        _factory.Uc
            .Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Club>());

        var response = await _client.GetAsync("/api/clubs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }
}
