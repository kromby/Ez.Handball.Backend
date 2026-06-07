using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed class PlayerSalaryService : IPlayerSalaryService
{
    private readonly IPlayerStatsAggregator _aggregator;
    private readonly IScoringRuleSetRepository _scoring;
    private readonly ISalaryRuleSetRepository _prices;
    private readonly FantasyPlayerRatingFunction _points;

    public PlayerSalaryService(
        IPlayerStatsAggregator aggregator,
        IScoringRuleSetRepository scoring,
        ISalaryRuleSetRepository prices,
        FantasyPlayerRatingFunction points)
    {
        _aggregator = aggregator;
        _scoring = scoring;
        _prices = prices;
        _points = points;
    }

    public async Task<PlayerSalary?> GetSalaryAsync(
        string playerId, int version, string? season, string? tournamentId, CancellationToken ct)
    {
        var scoringVersion = _points.DefaultRuleSetVersion!.Value; // fantasy scoring default (1)
        var scoring = await _scoring.GetAsync(GameFlavor.Fantasy, scoringVersion, ct);
        if (scoring is null) return null;

        var priceRuleSet = await _prices.GetAsync(version, ct);
        if (priceRuleSet is null) return null;

        var stats = await _aggregator.AggregateAsync(playerId, season, tournamentId, ct);
        var ctx = new PlayerRatingContext(season, tournamentId, null, null, null);
        var points = _points.Compute(new PlayerRatingInputs(playerId, stats, scoring, ctx)).Rating;

        var score = stats.Games >= priceRuleSet.MinGames && stats.Games > 0
            ? points / stats.Games
            : 0;

        var band = priceRuleSet.BandFor(score);
        return new PlayerSalary(
            playerId, new PlayerCost(band.Price, priceRuleSet.Currency), score, stats.Games, priceRuleSet.Name);
    }
}
