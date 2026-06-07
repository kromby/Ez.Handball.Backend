using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record GetPlayerRatingResult
{
    public sealed record NotFound : GetPlayerRatingResult;
    public sealed record InvalidFlavor : GetPlayerRatingResult;
    public sealed record RuleSetNotFound : GetPlayerRatingResult;
    public sealed record Found(PlayerRating Rating) : GetPlayerRatingResult;
}

public interface IGetPlayerRatingUseCase
{
    Task<GetPlayerRatingResult> ExecuteAsync(
        string playerId, GameFlavor flavor, PlayerRatingContext context, CancellationToken ct);
}

public class GetPlayerRatingUseCase : IGetPlayerRatingUseCase
{
    private readonly IReadOnlyDictionary<GameFlavor, IPlayerRatingFunction> _functions;
    private readonly IPlayerRepository _players;
    private readonly IPlayerStatsRepository _stats;
    private readonly ISeasonRepository _seasons;
    private readonly IScoringRuleSetRepository _ruleSets;

    public GetPlayerRatingUseCase(
        IEnumerable<IPlayerRatingFunction> functions,
        IPlayerRepository players,
        IPlayerStatsRepository stats,
        ISeasonRepository seasons,
        IScoringRuleSetRepository ruleSets)
    {
        _functions = functions.ToDictionary(f => f.Flavor);
        _players = players;
        _stats = stats;
        _seasons = seasons;
        _ruleSets = ruleSets;
    }

    public async Task<GetPlayerRatingResult> ExecuteAsync(
        string playerId, GameFlavor flavor, PlayerRatingContext context, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerRatingResult.NotFound();

        if (!_functions.TryGetValue(flavor, out var function))
            return new GetPlayerRatingResult.InvalidFlavor();

        var season = await ResolveSeasonAsync(context.Season, ct);
        var stats = await AggregateAsync(playerId, season, context.TournamentId, ct);

        ScoringRuleSet? ruleSet = null;
        if (function.DefaultRuleSetVersion is int defaultVersion)
        {
            var version = context.RuleSetVersion ?? defaultVersion;
            ruleSet = await _ruleSets.GetAsync(flavor, version, ct);
            if (ruleSet is null) return new GetPlayerRatingResult.RuleSetNotFound();
        }

        var value = function.Compute(new PlayerRatingInputs(playerId, stats, ruleSet, context));
        return new GetPlayerRatingResult.Found(value);
    }

    private async Task<string?> ResolveSeasonAsync(string? season, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(season)) return season;
        var all = await _seasons.ListAsync(ct);
        return all.FirstOrDefault(s => s.IsCurrent)?.Label;
    }

    private async Task<AggregatedStats> AggregateAsync(
        string playerId, string? season, string? tournamentId, CancellationToken ct)
    {
        if (season is null) return new AggregatedStats(0, 0, 0, 0, 0);

        var rows = await _stats.GetByPlayerAsync(playerId, ct);
        var scoped = rows.Where(r => r.Season == season);
        if (!string.IsNullOrWhiteSpace(tournamentId))
            scoped = scoped.Where(r => r.TournamentId == tournamentId);

        var list = scoped.ToList();
        return new AggregatedStats(
            Games: list.Count,
            Goals: list.Sum(r => r.Goals),
            YellowCards: list.Sum(r => r.YellowCards),
            TwoMinuteSuspensions: list.Sum(r => r.TwoMinuteSuspensions),
            RedCards: list.Sum(r => r.RedCards));
    }
}
