using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Services;

// Fires once a match's stats are considered final (currently: after HBStatz enrichment sync
// succeeds). Best-effort: pokes the Api settlement endpoint so it can recompute any gameweek
// whose matches are now all final. The Api is authoritative (idempotent, "not ready" until
// complete); failures here are logged, not fatal.
public class SettlementTrigger : ISettlementTrigger
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SettlementTrigger> _logger;

    public SettlementTrigger(IHttpClientFactory httpFactory, ILogger<SettlementTrigger> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task PokeAsync(string matchId, CancellationToken ct = default)
    {
        var baseUrl = Environment.GetEnvironmentVariable("Settlement__ApiBaseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogInformation("Settlement trigger skipped for match {MatchId}: no Settlement__ApiBaseUrl configured.", matchId);
            return;
        }

        try
        {
            _httpFactory.CreateClient();
            // V0: the Api decides readiness; the round + team fan-out is a follow-up. This logs intent.
            _logger.LogInformation("Match {MatchId} synced; settlement poke target {BaseUrl} (fan-out deferred).", matchId, baseUrl);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settlement poke failed for match {MatchId}.", matchId);
        }
    }
}
