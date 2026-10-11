using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Application.Services;

public interface ITransferCountRecorder
{
    // Records a committed buy/sell against the round it applies to. Never throws.
    Task RecordAsync(string teamId, string roundLabel, TransferType type, DateTimeOffset now, CancellationToken ct);
}

public sealed class TransferCountRecorder : ITransferCountRecorder
{
    private readonly ITransferCountRepository _counts;
    private readonly ILogger<TransferCountRecorder> _logger;

    public TransferCountRecorder(ITransferCountRepository counts, ILogger<TransferCountRecorder> logger)
    {
        _counts = counts;
        _logger = logger;
    }

    public async Task RecordAsync(
        string teamId, string roundLabel, TransferType type, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await _counts.IncrementAsync(teamId, roundLabel, type, now, ct);
        }
        catch (Exception ex)
        {
            // The transfer has already committed (budget, roster, ledger), so failing the request
            // now would invite a retry that hits duplicate_player. A missed count under-charges
            // the team's point hit for this round, so log it loudly rather than as a warning.
            _logger.LogError(ex,
                "Failed to count {Type} for team {TeamId} in round {RoundLabel}; its transfer allowance is now under-counted",
                type, teamId, roundLabel);
        }
    }
}
