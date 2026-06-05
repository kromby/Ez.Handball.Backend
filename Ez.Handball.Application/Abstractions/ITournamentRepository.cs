using Ez.Handball.Domain;

namespace Ez.Handball.Application.Abstractions;

public interface ITournamentRepository
{
    Task<IReadOnlyList<Tournament>> ListEnabledBySeasonAsync(string season, CancellationToken ct);
}
