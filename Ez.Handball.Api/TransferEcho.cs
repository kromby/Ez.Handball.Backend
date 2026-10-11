using Ez.Handball.Application.UseCases;

namespace Ez.Handball.Api;

// The current round's transfer allowance (#169/#170), shared by the squad read and the buy/sell
// responses. On buy/sell it's read after the commit, so it already includes that transfer.
// Null when there's no allowance to report (no transfer/gameweek config, or no open round).
public static class TransferEcho
{
    public static async Task<object?> BuildAsync(IGetTransferAllowanceUseCase uc, string userId, CancellationToken ct)
        => await uc.ExecuteAsync(userId, null, ct) is GetTransferAllowanceResult.Found { Allowance: var a }
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
