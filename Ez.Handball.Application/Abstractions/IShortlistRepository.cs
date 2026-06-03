using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IShortlistRepository
{
    // Raw row including DeletedAt, or null if no row exists.
    Task<ShortlistEntry?> GetAsync(string userId, string playerId, CancellationToken ct);

    // Count of entries with DeletedAt == null.
    Task<int> CountActiveAsync(string userId, CancellationToken ct);

    // Single Replace upsert: add/reactivate (deletedAt: null) or soft-delete (deletedAt: now).
    Task UpsertAsync(string userId, string playerId, DateTimeOffset createdAt, DateTimeOffset? deletedAt, CancellationToken ct);

    // Active rows only (DeletedAt == null).
    Task<IReadOnlyList<ShortlistEntry>> ListActiveAsync(string userId, CancellationToken ct);
}
