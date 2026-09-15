namespace Ez.Handball.Domain;

public sealed record ScoringRuleSet(
    GameFlavor Flavor,
    int Version,
    double GoalPoints,
    double YellowCardPoints,
    double TwoMinutePoints,
    double RedCardPoints,
    double AppearancePoints,
    double AssistPoints = 0,
    double StealPoints = 0,
    double BlockPoints = 0,
    double SavePoints = 0)
{
    public string Name => $"{Flavor.ToString().ToLowerInvariant()}-v{Version}";
}
