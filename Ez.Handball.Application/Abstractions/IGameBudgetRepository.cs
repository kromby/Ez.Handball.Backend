namespace Ez.Handball.Application.Abstractions;

// The team's stored cash balance. ETag concurrency lives inside the implementation, so the
// Application layer never touches Azure types.
public interface IGameBudgetRepository
{
    // Current balance, or null if the budget row does not exist.
    Task<double?> GetBalanceAsync(string teamId, CancellationToken ct);

    // Create/overwrite the balance row (used at provisioning/migration time).
    Task CreateAsync(string teamId, double amount, DateTimeOffset now, CancellationToken ct);

    // Atomically subtract `amount` (ETag retry). Returns false if the row is missing, the
    // balance would go negative, or the write keeps losing the race after retries.
    Task<bool> TryDeductAsync(string teamId, double amount, DateTimeOffset now, CancellationToken ct);

    // Atomically add `amount` (ETag retry). Returns false only if the row is missing or the
    // write keeps losing the race.
    Task<bool> TryCreditAsync(string teamId, double amount, DateTimeOffset now, CancellationToken ct);
}
