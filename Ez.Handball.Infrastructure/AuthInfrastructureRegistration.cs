using Azure.Communication.Email;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.Email;
using Ez.Handball.Infrastructure.Security;
using Ez.Handball.Infrastructure.TableAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ez.Handball.Infrastructure;

public static class AuthInfrastructureRegistration
{
    // Sibling to AddTableStorageInfrastructure. Assumes TableServiceClient is already registered.
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        var jwt = new JwtSettings(
            SigningKey: config["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey is required"),
            Issuer: config["Jwt:Issuer"] ?? "ez-handball",
            Audience: config["Jwt:Audience"] ?? "ez-handball-web",
            AccessTokenMinutes: config.GetValue("Jwt:AccessTokenMinutes", 15),
            RefreshTokenDays: config.GetValue("Auth:RefreshTokenDays", 30),
            EmailTokenHours: config.GetValue("Auth:EmailTokenHours", 24));
        services.AddSingleton(jwt);

        services.AddSingleton(new AuthSettings(
            config["Auth:VerificationUrlTemplate"] ?? "http://localhost/verify?token={token}",
            config["Auth:ResetUrlTemplate"] ?? "http://localhost/reset?token={token}",
            config["Auth:InviteUrlTemplate"] ?? "http://localhost/join?token={token}"));

        services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);

        services.AddScoped<IClubRepository, TableClubRepository>();
        services.AddScoped<IUserRepository, TableUserRepository>();
        services.AddScoped<IRefreshTokenRepository, TableRefreshTokenRepository>();
        services.AddScoped<IEmailTokenRepository, TableEmailTokenRepository>();

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // Development always renders through the console (safe to see real token-bearing links
        // locally); otherwise use the real ACS provider once Email:ConnectionString is configured
        // (e.g. via Azure App Service application settings), and fall back to a safe no-op until then.
        var emailConnectionString = config["Email:ConnectionString"];
        if (isDevelopment)
        {
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        }
        else if (!string.IsNullOrWhiteSpace(emailConnectionString))
        {
            var fromAddress = config["Email:FromAddress"];
            if (string.IsNullOrWhiteSpace(fromAddress))
                throw new InvalidOperationException("Email:FromAddress is required when Email:ConnectionString is set");
            services.AddSingleton(new EmailClient(emailConnectionString));
            services.AddSingleton<IEmailSender>(sp => new AcsEmailSender(sp.GetRequiredService<EmailClient>(), fromAddress));
        }
        else
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();
        }

        return services;
    }
}
