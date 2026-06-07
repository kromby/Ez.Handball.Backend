using Ez.Handball.Domain;

namespace Ez.Handball.Application.BuyFunctions;

// Deliberate stub. The manager game does not use the salary-cap model — parity with
// ManagerPlayerRatingFunction. Always allows, with a placeholder zero cost.
public sealed class ManagerBuyPlayerFunction : IBuyPlayerFunction
{
    public GameFlavor Flavor => GameFlavor.Manager;

    public BuyDecision Evaluate(BuyPlayerInputs inputs) =>
        new(inputs.PlayerId, "manager", true, new PlayerCost(0, "ISK"),
            System.Array.Empty<BuyRuleViolation>(), "manager-v0");
}
