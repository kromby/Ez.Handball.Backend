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

        var label = await ResolveSeasonLabelAsync(season, ct);
        if (string.IsNullOrWhiteSpace(label))
            return Array.Empty<string>();

        var tournaments = await _tournaments.ListBySeasonAsync(label, ct);
        return tournaments
            .Where(t => competitionId is null || t.CompetitionId == competitionId)
            .Where(t => type is null || t.Type == type)
            .Select(t => t.TournamentId)
            .ToList();
    }

    public async Task<string?> ResolveSeasonLabelAsync(string? season, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(season)) return season;
        var seasons = await _seasons.ListAsync(ct);
        return seasons.FirstOrDefault(s => s.IsCurrent)?.Label;
    }

    public async Task<PreviousSeasonScope?> ResolvePreviousSeasonScopeAsync(
        string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct)
    {
        var currentLabel = await ResolveSeasonLabelAsync(season, ct);
        if (string.IsNullOrWhiteSpace(currentLabel)) return null;

        // ISeasonRepository.ListAsync returns seasons newest-first (see
        // TableSeasonRepository) — "previous" is the entry right after current.
        var seasons = await _seasons.ListAsync(ct);
        var currentIndex = -1;
        for (var i = 0; i < seasons.Count; i++)
        {
            if (seasons[i].Label == currentLabel) { currentIndex = i; break; }
        }
        if (currentIndex < 0 || currentIndex + 1 >= seasons.Count) return null;
        var previousLabel = seasons[currentIndex + 1].Label;

        var effectiveCompetitionId = competitionId;
        if (string.IsNullOrWhiteSpace(effectiveCompetitionId) && !string.IsNullOrWhiteSpace(tournamentId))
        {
            var currentTournaments = await _tournaments.ListBySeasonAsync(currentLabel, ct);
            effectiveCompetitionId = currentTournaments
                .FirstOrDefault(t => t.TournamentId == tournamentId)?.CompetitionId;

            if (effectiveCompetitionId is null)
                return new PreviousSeasonScope(previousLabel, Array.Empty<string>());
        }

        var previousIds = await ResolveTournamentIdsAsync(previousLabel, null, effectiveCompetitionId, type, ct);
        return new PreviousSeasonScope(previousLabel, previousIds);
    }
}
