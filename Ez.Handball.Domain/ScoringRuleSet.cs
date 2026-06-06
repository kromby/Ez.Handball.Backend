namespace Ez.Handball.Domain;

public sealed record ScoringRuleSet(
    ValueFlavor Flavor,
    int Version,
    double GoalPoints,
    double YellowCardPoints,
    double TwoMinutePoints,
    double RedCardPoints,
    double AppearancePoints)
{
    public string Name => $"{Flavor.ToString().ToLowerInvariant()}-v{Version}";
}
