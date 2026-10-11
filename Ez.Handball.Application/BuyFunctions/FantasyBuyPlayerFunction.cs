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

        if (ExceedsPositionLimits(inputs))
            violations.Add(new("position_limit", PositionLimitMessage(inputs)));

        return new BuyDecision(
            inputs.PlayerId, "fantasy", violations.Count == 0, inputs.Cost, violations, inputs.Version);
    }

    // #108: a buy fits if some assignment of every player (squad + candidate) to one of their
    // eligible positions stays within the caps. A squad already over a cap (positions changed
    // after purchase) can't be solved at all, so it falls back to the old primary-only count —
    // caps only ever gate new buys.
    private static bool ExceedsPositionLimits(BuyPlayerInputs inputs)
    {
        var limits = inputs.Constraints.PositionLimits;
        if (limits.Count == 0) return false;

        var maxOnly = PositionAssignment.MaxOnly(limits);
        var squad = inputs.Squad.Players
            .Select(p => new PositionCandidate(p.PlayerId, p.EligiblePositions))
            .ToList();

        // Over-cap fallback deliberately counts buy-time snapshot primaries and ignores the
        // candidate's secondary (legacy behaviour for an already-illegal squad).
        if (PositionAssignment.TrySolve(squad, maxOnly) is null)
            return inputs.Position is { } position
                && limits.TryGetValue(position, out var limit)
                && inputs.Squad.Players.Count(p => p.Position == position) >= limit;

        var candidate = new PositionCandidate(
            inputs.PlayerId, inputs.Eligible ?? PositionAssignment.EligibleFor(inputs.Position, null));
        return PositionAssignment.TrySolve(squad.Append(candidate).ToList(), maxOnly) is null;
    }

    private static string PositionLimitMessage(BuyPlayerInputs inputs)
        => inputs.Position is { } position && inputs.Constraints.PositionLimits.TryGetValue(position, out var limit)
            ? $"Squad already has the maximum of {limit} players in position {position}"
            : "Squad position limits cannot accommodate this player";
}
