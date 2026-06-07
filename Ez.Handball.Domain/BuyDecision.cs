namespace Ez.Handball.Domain;

public sealed record BuyRuleViolation(string Code, string Message);

public sealed record BuyDecision(
    string PlayerId,
    string Flavor,                                  // lowercased flavor name, e.g. "fantasy"
    bool Allowed,
    PlayerCost Cost,
    IReadOnlyList<BuyRuleViolation> Violations,
    string Version);
