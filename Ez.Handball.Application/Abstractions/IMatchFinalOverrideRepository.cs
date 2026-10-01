namespace Ez.Handball.Application.Abstractions;

// Admin "treat as final" overrides for played matches hsi.is never marks final (Backend#147).
public interface IMatchFinalOverrideRepository
{
    Task<IReadOnlySet<string>> ListMatchIdsAsync(string tournamentId, CancellationToken ct);

    // Idempotent: setting an existing override refreshes who/when; clearing a missing one is a no-op.
    Task SetAsync(string tournamentId, string matchId, string setBy, DateTimeOffset setAt, CancellationToken ct);
    Task ClearAsync(string tournamentId, string matchId, CancellationToken ct);
}
