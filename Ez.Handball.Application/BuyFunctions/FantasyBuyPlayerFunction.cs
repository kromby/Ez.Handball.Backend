using Ez.Handball.Domain;

namespace Ez.Handball.Application.BuyFunctions;

public sealed class FantasyBuyPlayerFunction : IBuyPlayerFunction
{
    public GameFlavor Flavor => GameFlavor.Fantasy;

    public BuyDecision Evaluate(BuyPlayerInputs inputs)
    {
        var squad = inputs.Squad;
        var constraints = inputs.Constraints;
        var violations = new List<BuyRuleViolation>();

        if (inputs.Cost.Amount > squad.Budget)
            violations.Add(new("insufficient_budget",
                $"Cost {inputs.Cost.Amount:N0} exceeds remaining budget {squad.Budget:N0}"));

        if (squad.Players.Count >= constraints.MaxSquadSize)
            violations.Add(new("squad_full",
                $"Squad already has the maximum of {constraints.MaxSquadSize} players"));

        if (squad.Players.Any(p => p.PlayerId == inputs.PlayerId))
            violations.Add(new("duplicate_player", "Player is already in the squad"));

        if (inputs.Position is { } position
            && constraints.PositionLimits.TryGetValue(position, out var limit)
            && squad.Players.Count(p => p.Position == position) >= limit)
            violations.Add(new("position_limit",
                $"Squad already has the maximum of {limit} players in position {position}"));

        return new BuyDecision(
            inputs.PlayerId, "fantasy", violations.Count == 0, inputs.Cost, violations, inputs.Version);
    }
}
