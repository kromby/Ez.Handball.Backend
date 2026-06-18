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
        services.AddScoped<IPlayerPoolRepository, TablePlayerPoolRepository>();
        services.AddScoped<IMatchRepository, TableMatchRepository>();
        services.AddScoped<IMatchPlayerLinesRepository, TableMatchPlayerLinesRepository>();
        services.AddScoped<IShortlistRepository, TableShortlistRepository>();
        services.AddScoped<ISeasonRepository, TableSeasonRepository>();
        services.AddScoped<ITournamentRepository, TableTournamentRepository>();
        services.AddScoped<IScoringRuleSetRepository, TableScoringRuleSetRepository>();
        services.AddScoped<IPriceRuleSetRepository, TablePriceRuleSetRepository>();
        services.AddScoped<ISquadConstraintsRepository, TableSquadConstraintsRepository>();
        services.AddScoped<ISquadRepository, TableSquadRepository>();
        services.AddScoped<IGameTeamRepository, TableGameTeamRepository>();
        services.AddScoped<IGameTeamNameIndexRepository, TableGameTeamNameIndexRepository>();
        services.AddScoped<IGameBudgetRepository, TableGameBudgetRepository>();
        services.AddScoped<IGameRosterRepository, TableGameRosterRepository>();
        services.AddScoped<ITransferLedgerRepository, TableTransferLedgerRepository>();
        services.AddScoped<ILineupRepository, TableLineupRepository>();
        services.AddScoped<ILineupConstraintsRepository, TableLineupConstraintsRepository>();
        services.AddScoped<IMiniLeagueRepository, TableMiniLeagueRepository>();
        services.AddScoped<IMiniLeagueInviteRepository, TableMiniLeagueInviteRepository>();
        services.AddScoped<INotificationPreferenceRepository, TableNotificationPreferenceRepository>();
        services.AddScoped<INotificationChannel, LoggingNotificationChannel>();
        services.AddScoped<IGameweekConfigRepository, TableGameweekConfigRepository>();
        services.AddScoped<IGameweekLockRepository, TableGameweekLockRepository>();
        services.AddScoped<IGameweekLineupRepository, TableGameweekLineupRepository>();
        services.AddScoped<IGameweekScoreRepository, TableGameweekScoreRepository>();
        services.AddScoped<IClockOverrideStore>(sp =>
            new TableClockOverrideStore(sp.GetRequiredService<TableServiceClient>()));
        return services;
    }
}
