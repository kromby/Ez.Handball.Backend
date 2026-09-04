using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Tests.Infrastructure;

public class AuthInfrastructureRegistrationTests
{
    // EmailClient's constructor only parses the connection string; it makes no network call, so a
    // syntactically-valid fake string is safe to construct in a unit test.
    private const string FakeAcsConnectionString =
        "endpoint=https://example.communication.azure.com/;accesskey=fake-key-value";

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IEmailSender ResolveEmailSender(IConfiguration config, bool isDevelopment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthInfrastructure(config, isDevelopment);
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    [Fact]
    public void Development_ResolvesConsoleEmailSender()
    {
        var config = BuildConfig(new() { ["Jwt:SigningKey"] = "test-signing-key" });

        var sender = ResolveEmailSender(config, isDevelopment: true);

        Assert.IsType<ConsoleEmailSender>(sender);
    }

    [Fact]
    public void NonDevelopment_NoConnectionString_ResolvesNoopEmailSender()
    {
        var config = BuildConfig(new() { ["Jwt:SigningKey"] = "test-signing-key" });

        var sender = ResolveEmailSender(config, isDevelopment: false);

        Assert.IsType<NoopEmailSender>(sender);
    }

    [Fact]
    public void NonDevelopment_ConnectionStringAndFromAddressSet_ResolvesAcsEmailSender()
    {
        var config = BuildConfig(new()
        {
            ["Jwt:SigningKey"] = "test-signing-key",
            ["Email:ConnectionString"] = FakeAcsConnectionString,
            ["Email:FromAddress"] = "noreply@example.com",
        });

        var sender = ResolveEmailSender(config, isDevelopment: false);

        Assert.IsType<AcsEmailSender>(sender);
    }

    [Fact]
    public void NonDevelopment_ConnectionStringSet_FromAddressMissing_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Jwt:SigningKey"] = "test-signing-key",
            ["Email:ConnectionString"] = FakeAcsConnectionString,
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddAuthInfrastructure(config, isDevelopment: false));
        Assert.Contains("Email:FromAddress", ex.Message);
    }

    [Fact]
    public void NonDevelopment_ConnectionStringSet_FromAddressBlank_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Jwt:SigningKey"] = "test-signing-key",
            ["Email:ConnectionString"] = FakeAcsConnectionString,
            ["Email:FromAddress"] = "   ",
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddAuthInfrastructure(config, isDevelopment: false));
        Assert.Contains("Email:FromAddress", ex.Message);
    }
}
