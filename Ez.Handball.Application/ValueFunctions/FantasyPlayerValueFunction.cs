using Ez.Handball.Domain;

namespace Ez.Handball.Application.ValueFunctions;

public sealed class FantasyPlayerValueFunction : IPlayerValueFunction
{
    public ValueFlavor Flavor => ValueFlavor.Fantasy;
    public int? DefaultRuleSetVersion => 1;

    public PlayerValue Compute(PlayerValueInputs inputs)
    {
        var rs = inputs.RuleSet
            ?? throw new InvalidOperationException("Fantasy value requires a scoring rule set.");
        var s = inputs.Stats;

        var components = new List<PlayerValueComponent>
        {
            Component("goals",       s.Goals,                rs.GoalPoints),
            Component("appearances", s.Games,                rs.AppearancePoints),
            Component("yellowCards", s.YellowCards,          rs.YellowCardPoints),
            Component("twoMinute",   s.TwoMinuteSuspensions, rs.TwoMinutePoints),
            Component("redCards",    s.RedCards,             rs.RedCardPoints),
        };

        var value = components.Sum(c => c.Contribution);

        return new PlayerValue(inputs.PlayerId, "fantasy", value, components, rs.Name);
    }

    private static PlayerValueComponent Component(string key, int count, double weight) =>
        new(key, count, weight, count * weight);
}
