using Ez.Handball.Domain;

namespace Ez.Handball.Application.RatingFunctions;

public sealed record PlayerRatingInputs(
    string PlayerId,
    AggregatedStats Stats,
    ScoringRuleSet? RuleSet,   // null when the flavor needs no rule set
    PlayerRatingContext Context);

public interface IPlayerRatingFunction
{
    GameFlavor Flavor { get; }
    int? DefaultRuleSetVersion { get; } // fantasy => 1; manager => null (stub)
    PlayerRating Compute(PlayerRatingInputs inputs);
}
