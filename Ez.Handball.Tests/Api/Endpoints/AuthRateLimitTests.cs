using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ez.Handball.Tests.Api.Endpoints;

public class AuthRateLimitTests : IClassFixture<AuthRateLimitTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Jwt:SigningKey"] = "integration-test-signing-key-32-bytes-min!!",
                    ["Auth:RateLimit:SensitivePermitLimit"] = "2",
                    ["Auth:RateLimit:WindowSeconds"] = "60"
                });
            });
            return base.CreateHost(builder);
        }
    }

    private readonly HttpClient _client;

    public AuthRateLimitTests(Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task SensitiveEndpoint_ExceedingLimit_Returns429()
    {
        await _client.PostAsJsonAsync("/api/auth/password/reset-request", new { email = "a@b.is" });
        await _client.PostAsJsonAsync("/api/auth/password/reset-request", new { email = "a@b.is" });
        var third = await _client.PostAsJsonAsync("/api/auth/password/reset-request", new { email = "a@b.is" });

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
