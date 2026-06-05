using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Ez.Handball.Tests.Api.Endpoints;

public class GendersEndpointTests : IClassFixture<GendersEndpointTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            return base.CreateHost(builder);
        }
    }

    private readonly HttpClient _client;

    public GendersEndpointTests(Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Get_ReturnsTheTwoGenders_With200AndExpectedShape()
    {
        var response = await _client.GetAsync("/api/genders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(2, body.GetArrayLength());

        Assert.Equal("karlar", body[0].GetProperty("value").GetString());
        Assert.Equal("Karlar", body[0].GetProperty("label").GetString());
        Assert.Equal("kvenna", body[1].GetProperty("value").GetString());
        Assert.Equal("Kvenna", body[1].GetProperty("label").GetString());
    }
}
