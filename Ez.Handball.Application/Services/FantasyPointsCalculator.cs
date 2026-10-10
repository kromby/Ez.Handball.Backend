using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.Services;

// Scores any slice of a player's games (one match, one season) with the same rating
// function and rule set the player pool uses, so "Stig" means the same thing everywhere.
public sealed class FantasyPointsCalculator
{
    private readonly FantasyPlayerRatingFunction _rating;
    private readonly IScoringRuleSetRepository _scoring;

    public FantasyPointsCalculator(FantasyPlayerRatingFunction rating, IScoringRuleSetRepository scoring)
    {
        _rating = rating;
        _scoring = scoring;
    }

    public Task<ScoringRuleSet?> LoadRuleSetAsync(CancellationToken ct) =>
        _scoring.GetAsync(GameFlavor.Fantasy, _rating.DefaultRuleSetVersion!.Value, ct);

    public double? Score(string playerId, AggregatedStats stats, ScoringRuleSet? ruleSet)
    {
        if (ruleSet is null) return null;
        var context = new PlayerRatingContext(null, null, null, ruleSet.Version, null, null);
        return Whole(_rating.Compute(new PlayerRatingInputs(playerId, stats, ruleSet, context)).Rating);
    }

    // Scored points are whole numbers (#155). The only fractional rule value is 0.5 per save, so
    // rounding the total down is exactly "1 point per 2 saves". The unrounded rating still drives
    // pricing (FantasyPricing), so prices don't move.
    public static double Whole(double rating) => Math.Floor(rating);
}
