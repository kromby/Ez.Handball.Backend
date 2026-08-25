using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.BlobAccess;
using Ez.Handball.Infrastructure.Ingestion;
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

    // Sibling to AddTableStorageInfrastructure — reads the raw hsi.is archive the
    // ingestion pipeline writes, for the admin game-status cross-check.
    public static IServiceCollection AddBlobStorageInfrastructure(
        this IServiceCollection services, string connectionString, string containerName)
    {
        services.AddSingleton(_ => new BlobServiceClient(connectionString));
        services.AddSingleton<IBlobReader>(sp =>
            new BlobReader(sp.GetRequiredService<BlobServiceClient>(), containerName));
        services.AddScoped<IMatchScheduleRepository, BlobMatchScheduleRepository>();
        return services;
    }

    // Sibling to AddBlobStorageInfrastructure — lets the admin UI trigger a sync run on the
    // separately-deployed Ingestion Functions app. functionKey is required against a deployed
    // Function App (AuthorizationLevel.Function); local `func start` ignores it.
    public static IServiceCollection AddIngestionTriggerInfrastructure(
        this IServiceCollection services, string baseUrl, string? functionKey)
    {
        services.AddSingleton(new IngestionSettings(baseUrl, functionKey));
        services.AddHttpClient<IIngestionTrigger, HttpIngestionTrigger>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IngestionSettings>();
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(2); // sync loops over every Ingest=true tournament
        });
        return services;
    }
}
