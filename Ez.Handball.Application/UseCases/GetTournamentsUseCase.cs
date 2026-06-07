using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public interface IGetTournamentsUseCase
{
    Task<IReadOnlyList<Tournament>> ExecuteAsync(string? season, CancellationToken ct);
}

public class GetTournamentsUseCase : IGetTournamentsUseCase
{
    private readonly ITournamentRepository _tournaments;
    private readonly ISeasonRepository _seasons;

    public GetTournamentsUseCase(ITournamentRepository tournaments, ISeasonRepository seasons)
    {
        _tournaments = tournaments;
        _seasons = seasons;
    }

    public async Task<IReadOnlyList<Tournament>> ExecuteAsync(string? season, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            var seasons = await _seasons.ListAsync(ct);
            var current = seasons.FirstOrDefault(s => s.IsCurrent);
            if (current is null)
                return Array.Empty<Tournament>();
            season = current.Label;
        }

        return await _tournaments.ListActiveBySeasonAsync(season, ct);
    }
}
