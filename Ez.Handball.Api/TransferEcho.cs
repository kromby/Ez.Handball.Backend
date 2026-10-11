using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

// The current round's transfer allowance (#169/#170), shared by the squad read and the buy/sell
// responses. On buy/sell it's read after the commit, so it already includes that transfer.
// Null when there's no allowance to report (no transfer/gameweek config, or no open round).
public sealed class TransferEcho
{
    private readonly IGetTransferAllowanceUseCase _allowance;
    private readonly ILogger<TransferEcho> _logger;

    public TransferEcho(IGetTransferAllowanceUseCase allowance, ILogger<TransferEcho> logger)
    {
        _allowance = allowance;
        _logger = logger;
    }

    public async Task<object?> BuildAsync(string userId, CancellationToken ct)
    {
        try
        {
            return Body(await _allowance.ExecuteAsync(userId, null, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // On buy/sell the transfer has already committed: a 500 now would invite a retry that
            // hits duplicate_player. On the squad read, the squad is still worth showing. Either
            // way the allowance is extra information, so degrade to null and keep it visible.
            _logger.LogError(ex, "Failed to read the transfer allowance for user {UserId}", userId);
            return null;
        }
    }

    private static object? Body(GetTransferAllowanceResult result)
        => result is GetTransferAllowanceResult.Found { Allowance: var a }
            ? new
            {
                roundNumber = a.RoundNumber,
                roundLabel = a.RoundLabel,
                freeAvailable = a.FreeAvailable,
                used = a.Used,
                extra = a.Extra,
                pointsHit = a.PointsHit,
                exempt = a.Exempt
            }
            : null;
}
