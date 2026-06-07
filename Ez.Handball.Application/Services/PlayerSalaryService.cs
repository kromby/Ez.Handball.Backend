using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

public sealed class PlayerSalaryService : IPlayerSalaryService
{
    private readonly IPlayerStatsAggregator _aggregator;
    private readonly IScoringRuleSetRepository _scoring;
    private readonly ISalaryRuleSetRepository _prices;
    private readonly FantasyPricing _pricing;

    public PlayerSalaryService(
        IPlayerStatsAggregator aggregator,
        IScoringRuleSetRepository scoring,
        ISalaryRuleSetRepository prices,
        FantasyPricing pricing)
    {
        _aggregator = aggregator;
        _scoring = scoring;
        _prices = prices;
        _pricing = pricing;
    }

    public async Task<PlayerSalary?> GetSalaryAsync(
        string playerId, int version, string? season, string? tournamentId, CancellationToken ct)
    {
        var scoring = await _scoring.GetAsync(GameFlavor.Fantasy, _pricing.ScoringVersion, ct);
        if (scoring is null) return null;

        var priceRuleSet = await _prices.GetAsync(version, ct);
        if (priceRuleSet is null) return null;

        var stats = await _aggregator.AggregateAsync(playerId, season, tournamentId, null, null, ct);
        var ctx = new PlayerRatingContext(season, tournamentId, null, null, null, null);

        var result = _pricing.Compute(playerId, stats, scoring, priceRuleSet, ctx);
        return new PlayerSalary(
            playerId, result.Cost, result.Score, stats.Games, priceRuleSet.Name);
    }
}
