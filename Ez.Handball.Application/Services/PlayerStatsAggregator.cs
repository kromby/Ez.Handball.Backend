using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed class PlayerStatsAggregator : IPlayerStatsAggregator
{
    private readonly IPlayerStatsRepository _stats;
    private readonly ISeasonRepository _seasons;
    private readonly ITournamentScopeResolver _scope;

    public PlayerStatsAggregator(
        IPlayerStatsRepository stats, ISeasonRepository seasons, ITournamentScopeResolver scope)
    {
        _stats = stats;
        _seasons = seasons;
        _scope = scope;
    }

    public async Task<AggregatedStats> AggregateAsync(
        string playerId, string? season, string? tournamentId, string? competitionId,
        TournamentType? type, CancellationToken ct)
    {
        var resolved = await ResolveSeasonAsync(season, ct);
        if (resolved is null) return new AggregatedStats(0, 0, 0, 0, 0);

        var ids = await _scope.ResolveTournamentIdsAsync(resolved, tournamentId, competitionId, type, ct);

        var rows = await _stats.GetByPlayerAsync(playerId, ct);
        var scoped = rows.Where(r => r.Season == resolved);
        if (ids is not null)
            scoped = scoped.Where(r => ids.Contains(r.TournamentId));

        var list = scoped.ToList();
        return new AggregatedStats(
            Games: list.Count,
            Goals: list.Sum(r => r.Goals),
            YellowCards: list.Sum(r => r.YellowCards),
            TwoMinuteSuspensions: list.Sum(r => r.TwoMinuteSuspensions),
            RedCards: list.Sum(r => r.RedCards));
    }

    private async Task<string?> ResolveSeasonAsync(string? season, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(season)) return season;
        var all = await _seasons.ListAsync(ct);
        return all.FirstOrDefault(s => s.IsCurrent)?.Label;
    }
}
