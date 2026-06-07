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
        services.AddScoped<ILeaderboardRepository, TableLeaderboardRepository>();
        services.AddScoped<IMatchRepository, TableMatchRepository>();
        services.AddScoped<IMatchPlayerLinesRepository, TableMatchPlayerLinesRepository>();
        services.AddScoped<IShortlistRepository, TableShortlistRepository>();
        services.AddScoped<ISeasonRepository, TableSeasonRepository>();
        services.AddScoped<ITournamentRepository, TableTournamentRepository>();
        services.AddScoped<IScoringRuleSetRepository, TableScoringRuleSetRepository>();
        services.AddScoped<ISalaryRuleSetRepository, TableSalaryRuleSetRepository>();
        services.AddScoped<ISquadConstraintsRepository, TableSquadConstraintsRepository>();
        services.AddScoped<ISquadRepository, TableSquadRepository>();
        return services;
    }
}
