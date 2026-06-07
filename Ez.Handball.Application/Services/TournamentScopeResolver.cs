using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed class TournamentScopeResolver : ITournamentScopeResolver
{
    private readonly ITournamentRepository _tournaments;
    private readonly ISeasonRepository _seasons;

    public TournamentScopeResolver(ITournamentRepository tournaments, ISeasonRepository seasons)
    {
        _tournaments = tournaments;
        _seasons = seasons;
    }

    public async Task<IReadOnlyList<string>?> ResolveTournamentIdsAsync(
        string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct)
    {
        // A single explicit tournament wins and needs no season lookup.
        if (!string.IsNullOrWhiteSpace(tournamentId))
            return new[] { tournamentId };

        // No narrowing requested → caller scans the whole season.
        if (string.IsNullOrWhiteSpace(competitionId) && type is null)
            return null;

        var label = season;
        if (string.IsNullOrWhiteSpace(label))
        {
            var seasons = await _seasons.ListAsync(ct);
            label = seasons.FirstOrDefault(s => s.IsCurrent)?.Label;
            if (string.IsNullOrWhiteSpace(label))
                return Array.Empty<string>();
        }

        var tournaments = await _tournaments.ListBySeasonAsync(label, ct);
        return tournaments
            .Where(t => competitionId is null || t.CompetitionId == competitionId)
            .Where(t => type is null || t.Type == type)
            .Select(t => t.TournamentId)
            .ToList();
    }
}
