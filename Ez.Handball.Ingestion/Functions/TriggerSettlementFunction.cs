using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Ingestion.Functions;

// Fires after player stats are parsed (raw/matches/*/players-*.json). Best-effort: pokes the Api
// settlement endpoint so it can recompute any gameweek whose matches are now all final. The Api is
// authoritative (idempotent, "not ready" until complete); failures here are logged, not fatal.
public class TriggerSettlementFunction
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TriggerSettlementFunction> _logger;

    public TriggerSettlementFunction(IHttpClientFactory httpFactory, ILogger<TriggerSettlementFunction> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [Function("TriggerSettlement")]
    public async Task RunAsync(
        [BlobTrigger("raw/matches/{matchId}/{name}", Connection = "HandballStorageConnection")] string content,
        string matchId, string name, FunctionContext context)
    {
        if (!name.StartsWith("players-", StringComparison.Ordinal)) return;

        var baseUrl = Environment.GetEnvironmentVariable("Settlement__ApiBaseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogInformation("Settlement trigger skipped for match {MatchId}: no Settlement__ApiBaseUrl configured.", matchId);
            return;
        }

        try
        {
            var client = _httpFactory.CreateClient();
            // V0: the Api decides readiness; the round + team fan-out is a follow-up. This logs intent.
            _logger.LogInformation("Match {MatchId} parsed; settlement poke target {BaseUrl} (fan-out deferred).", matchId, baseUrl);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settlement poke failed for match {MatchId}.", matchId);
        }
    }
}
