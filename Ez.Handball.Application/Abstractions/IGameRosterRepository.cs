using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public enum RosterAddOutcome { Added, AlreadyActive }

public interface IGameRosterRepository
{
    // Active rows only (DeletedAt == null).
    Task<IReadOnlyList<RosterEntry>> ListActiveAsync(string teamId, CancellationToken ct);

    // The raw row including DeletedAt, or null if no row exists.
    Task<RosterEntry?> GetAsync(string teamId, string playerId, CancellationToken ct);

    // Insert a new row, or resurrect a soft-deleted one (clears DeletedAt, relocks PricePaid).
    // Returns AlreadyActive if an active row already exists (write-time duplicate guard).
    Task<RosterAddOutcome> AddOrResurrectAsync(
        string teamId, string playerId, string? position, double pricePaid, DateTimeOffset now, CancellationToken ct);

    // Soft-delete (DeletedAt = now). No-op if the row is missing.
    Task SoftDeleteAsync(string teamId, string playerId, DateTimeOffset now, CancellationToken ct);
}
