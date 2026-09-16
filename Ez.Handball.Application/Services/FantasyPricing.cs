using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

// The fantasy rating (#52 metric) + price band for a player, computed from
// ALREADY-aggregated stats. Pure: no I/O, no rule-set loading. Both the
// single-player price path and the bulk pool path call this so the formula
// lives in exactly one place.
//
// Below MinGames, the price-driving Score fades from last season's rate
// (same competition, w=0) toward the current season's own rate (w=1) as
// currentGames approaches BlendGames — rather than jumping straight from a
// forced-zero score to the full current rate. Rating stays current-season-only.
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
        PlayerRatingContext context,
        AggregatedStats? previousSeasonStats = null)
    {
        var rating = _rating.Compute(new PlayerRatingInputs(playerId, stats, scoring, context)).Rating;
        var currentRate = stats.Games > 0 ? rating / stats.Games : 0;

        double score;
        if (previousSeasonStats is { Games: var priorGames } prior && priorGames >= prices.MinGames)
        {
            // Context is accepted by the rating function but unused by the fantasy
            // formula (see GetPlayerPoolUseCase) — reusing the caller's context here
            // is safe for the same reason.
            var priorRating = _rating.Compute(new PlayerRatingInputs(playerId, prior, scoring, context)).Rating;
            var priorRate = priorRating / priorGames;
            var weight = Math.Min((double)stats.Games / prices.BlendGames, 1.0);
            score = weight * currentRate + (1 - weight) * priorRate;
        }
        else
        {
            score = stats.Games >= prices.MinGames && stats.Games > 0 ? currentRate : 0;
        }

        var band = prices.BandFor(score);
        return new FantasyPriceResult(rating, score, new PlayerPrice(band.Price, prices.Currency));
    }
}
