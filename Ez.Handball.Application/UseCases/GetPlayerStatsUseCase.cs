using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public sealed record PlayerStatsQuery(
    string? Season,
    string? TournamentId,
    string? CompetitionId,
    TournamentType? Type);

public abstract record GetPlayerStatsResult
{
    public sealed record NotFound : GetPlayerStatsResult;
    public sealed record Found(string PlayerId, IReadOnlyList<PlayerMatchStat> Stats) : GetPlayerStatsResult;
}

public interface IGetPlayerStatsUseCase
{
    Task<GetPlayerStatsResult> ExecuteAsync(string playerId, PlayerStatsQuery query, CancellationToken ct);
}

public class GetPlayerStatsUseCase : IGetPlayerStatsUseCase
{
    private readonly IPlayerRepository _players;
    private readonly IPlayerStatsRepository _stats;
    private readonly ITournamentScopeResolver _scope;
    private readonly IMatchRepository _matches;
    private readonly FantasyPointsCalculator _points;

    public GetPlayerStatsUseCase(
        IPlayerRepository players, IPlayerStatsRepository stats, ITournamentScopeResolver scope,
        IMatchRepository matches, FantasyPointsCalculator points)
    {
        _players = players;
        _stats = stats;
        _scope = scope;
        _matches = matches;
        _points = points;
    }

    public async Task<GetPlayerStatsResult> ExecuteAsync(
        string playerId, PlayerStatsQuery query, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerStatsResult.NotFound();

        var season = await _scope.ResolveSeasonLabelAsync(query.Season, ct);
        var ids = await _scope.ResolveTournamentIdsAsync(
            season, query.TournamentId, query.CompetitionId, query.Type, ct);

        IEnumerable<PlayerStat> rows = await _stats.GetByPlayerAsync(playerId, ct);
        if (!string.IsNullOrWhiteSpace(season))
            rows = rows.Where(r => r.Season == season);
        if (ids is not null)
            rows = rows.Where(r => ids.Contains(r.TournamentId));

        var scoped = rows.ToList();
        var matchesById = await LoadMatchesAsync(scoped, ct);
        var ruleSet = await _points.LoadRuleSetAsync(ct);

        var lines = scoped
            .Select(stat =>
            {
                var match = matchesById.GetValueOrDefault(stat.MatchId);
                return new PlayerMatchStat(
                    stat,
                    match?.Date,
                    match is null ? null : OpponentOf(match, stat.TeamId),
                    _points.Score(playerId, ToStats(stat), ruleSet));
            })
            .OrderByDescending(line => line.Date ?? DateTimeOffset.MinValue)
            .ToList();

        return new GetPlayerStatsResult.Found(playerId, lines);
    }

    // One listing per tournament rather than a lookup per match: a season is a handful
    // of tournaments but dozens of matches.
    private async Task<Dictionary<string, MatchListItem>> LoadMatchesAsync(
        IReadOnlyList<PlayerStat> stats, CancellationToken ct)
    {
        var byId = new Dictionary<string, MatchListItem>(StringComparer.Ordinal);
        foreach (var tournamentId in stats.Select(s => s.TournamentId).Distinct())
        {
            var listing = await _matches.ListByTournamentAsync(tournamentId, ct);
            foreach (var match in listing?.Matches ?? Array.Empty<MatchListItem>())
                byId[match.MatchId] = match;
        }
        return byId;
    }

    private static MatchListTeam? OpponentOf(MatchListItem match, string teamId) =>
        match.Home.TeamId == teamId ? match.Away
        : match.Away.TeamId == teamId ? match.Home
        : null;

    private static AggregatedStats ToStats(PlayerStat s) => new(
        1, s.Goals, s.YellowCards, s.TwoMinuteSuspensions, s.RedCards,
        s.HbStatzAssists ?? 0, s.HbStatzSteals ?? 0, s.HbStatzBlocks ?? 0, s.HbStatzSaves ?? 0);
}
