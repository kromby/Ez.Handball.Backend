using Azure.Data.Tables;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.TableAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Ez.Handball.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddTableStorageInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => new TableServiceClient(connectionString));
        services.AddSingleton<ITableQuery, TableQuery>();
        services.AddSingleton<Func<DateOnly>>(_ => () => DateOnly.FromDateTime(DateTime.UtcNow));
        services.AddScoped<IPlayerRepository, TablePlayerRepository>();
        services.AddScoped<IPlayerStatsRepository, TablePlayerStatsRepository>();
        services.AddScoped<IPlayerHistoryRepository, TablePlayerHistoryRepository>();
        services.AddScoped<IMatchRepository, TableMatchRepository>();
        services.AddScoped<IMatchPlayerLinesRepository, TableMatchPlayerLinesRepository>();
        return services;
    }
}
