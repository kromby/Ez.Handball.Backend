using Ez.Handball.Domain;

namespace Ez.Handball.Application.ValueFunctions;

public sealed record PlayerValueInputs(
    string PlayerId,
    AggregatedStats Stats,
    ScoringRuleSet? RuleSet,   // null when the flavor needs no rule set
    PlayerValueContext Context);

public interface IPlayerValueFunction
{
    GameFlavor Flavor { get; }
    int? DefaultRuleSetVersion { get; } // fantasy => 1; manager => null (stub)
    PlayerValue Compute(PlayerValueInputs inputs);
}
