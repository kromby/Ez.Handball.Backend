using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

// Persists a transfer event best-effort: a ledger write must never fail or roll back a
// successful buy/sell. Failures are swallowed and logged, never rethrown.
public interface ITransferLedgerRecorder
{
    Task RecordAsync(TransferEntry entry, CancellationToken ct);
}
