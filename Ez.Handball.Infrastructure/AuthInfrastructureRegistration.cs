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
        this IServiceCollection services, IConfiguration config)
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
            config["Auth:ResetUrlTemplate"] ?? "http://localhost/reset?token={token}"));

        services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);

        services.AddScoped<IClubRepository, TableClubRepository>();
        services.AddScoped<IUserRepository, TableUserRepository>();
        services.AddScoped<IRefreshTokenRepository, TableRefreshTokenRepository>();
        services.AddScoped<IEmailTokenRepository, TableEmailTokenRepository>();

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IEmailSender, ConsoleEmailSender>();

        return services;
    }
}
