using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

// The fantasy rating (#52 metric) + price band for a player, computed from
// ALREADY-aggregated stats. Pure: no I/O, no rule-set loading. Both the
// single-player price path and the bulk pool path call this so the formula
// lives in exactly one place.
public readonly record struct FantasyPriceResult(double Rating, double Score, PlayerPrice Price);

public sealed class FantasyPricing
{
    private readonly FantasyPlayerRatingFunction _rating;

    public FantasyPricing(FantasyPlayerRatingFunction rating) => _rating = rating;

    // The fantasy scoring rule-set version this pricing is built on.
    public int ScoringVersion => _rating.DefaultRuleSetVersion!.Value;

    public FantasyPriceResult Compute(
        string playerId,
        AggregatedStats stats,
        ScoringRuleSet scoring,
        PriceRuleSet prices,
        PlayerRatingContext context)
    {
        var rating = _rating.Compute(new PlayerRatingInputs(playerId, stats, scoring, context)).Rating;
        var score = stats.Games >= prices.MinGames && stats.Games > 0 ? rating / stats.Games : 0;
        var band = prices.BandFor(score);
        return new FantasyPriceResult(rating, score, new PlayerPrice(band.Price, prices.Currency));
    }
}
