using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api.Settlement;

public sealed class AutoSettlementOptions
{
    public const string Section = "Settlement";

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    // Rounds whose last match started within this window are re-settled on every tick, so late stats
    // (HBStatz enrichment, corrections) flow into scores. Older rounds are backfilled via the admin endpoint.
    public TimeSpan RecentWindow { get; set; } = TimeSpan.FromDays(7);
    // Keeps the first run off the startup path (and out of short-lived test hosts).
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(2);
}

// Periodically settles complete gameweeks (#136). Nothing else settles rounds in production: the
// ingestion SettlementTrigger never fanned out. Settlement is idempotent, so a scaled-out App Service
// running this on several instances only repeats work — it never corrupts scores.
public sealed class AutoSettlementService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AutoSettlementOptions _options;
    private readonly ILogger<AutoSettlementService> _logger;

    public AutoSettlementService(
        IServiceScopeFactory scopes, AutoSettlementOptions options, ILogger<AutoSettlementService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Automatic gameweek settlement is disabled.");
            return;
        }

        try
        {
            await Task.Delay(_options.StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(_options.Interval);
            do
            {
                await SettleOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private async Task SettleOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<ISettleCompletedRoundsUseCase>();
            var result = await useCase.ExecuteAsync(_options.RecentWindow, null, ct);
            if (result is SettleCompletedRoundsResult.Completed completed)
            {
                foreach (var report in completed.Rounds)
                    _logger.LogInformation(
                        "Settled round {Round}: {Settled} settled, {NotReady} not ready, {Skipped} skipped of {Teams} teams.",
                        report.Round, report.Settled, report.NotReady, report.Skipped, report.TeamsConsidered);
            }
            else
            {
                _logger.LogWarning("Automatic settlement did not complete: {Result}.", result);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One failed tick (e.g. storage blip) must not stop future ticks.
            _logger.LogError(ex, "Automatic settlement tick failed.");
        }
    }
}
