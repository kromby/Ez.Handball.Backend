using Ez.Handball.Domain;

namespace Ez.Handball.Application.BuyFunctions;

public sealed record BuyPlayerInputs(
    string PlayerId,
    string? Position,
    PlayerPrice Cost,                // the player's price (from the #52 primitive)
    string Version,                 // the pricing rule-set name, e.g. "fantasy-price-v1"
    SquadConstraints Constraints,   // from the fantasy-squad-v{n} Config group
    Squad Squad,
    BuyPlayerContext Context);

public interface IBuyPlayerFunction
{
    GameFlavor Flavor { get; }
    BuyDecision Evaluate(BuyPlayerInputs inputs);   // pure, no I/O
}
