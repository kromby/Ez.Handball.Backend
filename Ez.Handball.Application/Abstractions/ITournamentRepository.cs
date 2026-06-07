using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ITournamentRepository
{
    // Active=true rows only — drives the /api/tournaments display list.
    Task<IReadOnlyList<Tournament>> ListActiveBySeasonAsync(string season, CancellationToken ct);

    // All rows for the season, regardless of Ingest/Active — used by the
    // scope resolver to map competition/type → tournament ids.
    Task<IReadOnlyList<Tournament>> ListBySeasonAsync(string season, CancellationToken ct);
}
