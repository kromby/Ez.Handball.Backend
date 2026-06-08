using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface IMiniLeagueInviteRepository
{
    // Write the invite row (PartitionKey = "invite", RowKey = invite.Token).
    Task AddAsync(MiniLeagueInvite invite, CancellationToken ct);

    // Point read by token; null if no such invite. Used by join/preview.
    Task<MiniLeagueInvite?> GetByTokenAsync(string token, CancellationToken ct);

    // The single active invite for a league (0 or 1); null if none. Used by get-current/rotate.
    Task<MiniLeagueInvite?> GetByLeagueAsync(string leagueId, CancellationToken ct);

    // Delete an invite by token. Idempotent — a missing row is not an error.
    Task DeleteByTokenAsync(string token, CancellationToken ct);
}
