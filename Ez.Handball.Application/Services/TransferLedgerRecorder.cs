using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Application.Services;

public sealed class TransferLedgerRecorder : ITransferLedgerRecorder
{
    private readonly ITransferLedgerRepository _ledger;
    private readonly ILogger<TransferLedgerRecorder> _logger;

    public TransferLedgerRecorder(ITransferLedgerRepository ledger, ILogger<TransferLedgerRecorder> logger)
    {
        _ledger = ledger;
        _logger = logger;
    }

    public async Task RecordAsync(TransferEntry entry, CancellationToken ct)
    {
        try
        {
            await _ledger.AppendAsync(entry, ct);
        }
        catch (Exception ex)
        {
            // Non-critical analytics write: a successful transfer must never fail because of it.
            _logger.LogWarning(ex,
                "Failed to append transfer-ledger entry for user {UserId}, player {PlayerId}, type {Type}",
                entry.UserId, entry.PlayerId, entry.Type);
        }
    }
}
