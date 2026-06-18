using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ez.Handball.Tests.Api.Endpoints;

public class DebugReplayEndpointTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _settings;
        public Factory(bool enabled, string? adminKey)
        {
            _settings = new()
            {
                ["Debug:GameClock:OverrideEnabled"] = enabled ? "true" : "false",
                ["Debug:AdminKey"] = adminKey,
            };
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureHostConfiguration(c => c.AddInMemoryCollection(_settings));
            return base.CreateHost(builder);
        }
    }

    private static HttpRequestMessage Clear(string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/debug/clock")
        {
            Content = JsonContent.Create(new { mode = "clear" })
        };
        if (key is not null) req.Headers.Add("X-Debug-Key", key);
        return req;
    }

    [Fact]
    public async Task FlagOff_RouteNotMapped_Returns404()
    {
        using var factory = new Factory(enabled: false, adminKey: "secret");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear("secret"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FlagOn_MissingKey_Returns401()
    {
        using var factory = new Factory(enabled: true, adminKey: "secret");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear(key: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FlagOn_WrongKey_Returns401()
    {
        using var factory = new Factory(enabled: true, adminKey: "secret");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear("nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FlagOn_KeyNotConfigured_Returns403()
    {
        using var factory = new Factory(enabled: true, adminKey: null);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Clear("anything"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
