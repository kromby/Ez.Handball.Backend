using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ITransferLedgerRepository
{
    // Append-only insert of one buy/sell event.
    Task AppendAsync(TransferEntry entry, CancellationToken ct);

    // All events for the flavor with CreatedAt in [from, to]. Scans only the weekly
    // partition buckets spanning that range.
    Task<IReadOnlyList<TransferEntry>> ListSinceAsync(
        GameFlavor flavor, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
