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
    private readonly IPlayerStatsAggregator _aggregator;
    private readonly IScoringRuleSetRepository _ruleSets;

    public GetPlayerRatingUseCase(
        IEnumerable<IPlayerRatingFunction> functions,
        IPlayerRepository players,
        IPlayerStatsAggregator aggregator,
        IScoringRuleSetRepository ruleSets)
    {
        _functions = functions.ToDictionary(f => f.Flavor);
        _players = players;
        _aggregator = aggregator;
        _ruleSets = ruleSets;
    }

    public async Task<GetPlayerRatingResult> ExecuteAsync(
        string playerId, GameFlavor flavor, PlayerRatingContext context, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new GetPlayerRatingResult.NotFound();

        if (!_functions.TryGetValue(flavor, out var function))
            return new GetPlayerRatingResult.InvalidFlavor();

        var stats = await _aggregator.AggregateAsync(playerId, context.Season, context.TournamentId, ct);

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
}
