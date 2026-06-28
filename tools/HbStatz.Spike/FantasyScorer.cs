namespace HbStatz.Spike;

// Values mirror SeedScoringRuleSetsFunction.RuleSetDefinitions ("fantasy-v1").
public static class FantasyScorer
{
    public const double GoalPoints = 2;
    public const double YellowCardPoints = -1;
    public const double TwoMinutePoints = -2;
    public const double RedCardPoints = -5;
    public const double AppearancePoints = 1;

    public static double Score(PlayerStatLine line) =>
        line.Goals * GoalPoints
        + line.YellowCards * YellowCardPoints
        + line.TwoMinuteSuspensions * TwoMinutePoints
        + line.RedCards * RedCardPoints
        + AppearancePoints;
}
