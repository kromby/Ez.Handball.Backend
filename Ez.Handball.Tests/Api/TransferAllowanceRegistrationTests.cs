using Ez.Handball.Application.UseCases;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ez.Handball.Tests.Api;

// Endpoint tests replace use cases with mocks, so nothing else resolves these from the real
// container. A missing registration for the transfer-count dependencies (#168) would only show
// up as a 500 on buy/sell in production.
public class TransferAllowanceRegistrationTests : IClassFixture<TransferAllowanceRegistrationTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        // Program reads the storage connection string while registering services, before
        // in-memory configuration applies (same workaround as BuyDecisionEndpointTests).
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
                    ["Jwt:AccessTokenMinutes"] = "15"
                }));
            return base.CreateHost(builder);
        }
    }

    private readonly Factory _factory;

    public TransferAllowanceRegistrationTests(Factory factory) => _factory = factory;

    [Theory]
    [InlineData(typeof(IBuyPlayerUseCase))]
    [InlineData(typeof(ISellPlayerUseCase))]
    [InlineData(typeof(IGetTransferAllowanceUseCase))]
    [InlineData(typeof(Ez.Handball.Api.TransferEcho))]
    public void ResolvesFromTheApiContainer(Type service)
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(service));
    }
}
