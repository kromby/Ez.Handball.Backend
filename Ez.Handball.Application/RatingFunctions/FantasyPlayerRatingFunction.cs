using Ez.Handball.Domain;

namespace Ez.Handball.Application.RatingFunctions;

public sealed class FantasyPlayerRatingFunction : IPlayerRatingFunction
{
    public GameFlavor Flavor => GameFlavor.Fantasy;
    public int? DefaultRuleSetVersion => 2;

    public PlayerRating Compute(PlayerRatingInputs inputs)
    {
        var rs = inputs.RuleSet
            ?? throw new InvalidOperationException("Fantasy value requires a scoring rule set.");
        var s = inputs.Stats;

        var components = new List<PlayerRatingComponent>
        {
            Component("goals",       s.Goals,                rs.GoalPoints),
            Component("appearances", s.Games,                rs.AppearancePoints),
            Component("yellowCards", s.YellowCards,          rs.YellowCardPoints),
            Component("twoMinute",   s.TwoMinuteSuspensions, rs.TwoMinutePoints),
            Component("redCards",    s.RedCards,             rs.RedCardPoints),
        };

        // v1 consumers expect a fixed five-component shape — only append the HBStatz-derived
        // components for v2+ rule sets, rather than always including them at zero for v1.
        if (rs.Version >= 2)
        {
            components.Add(Component("assists", s.Assists, rs.AssistPoints));
            components.Add(Component("steals",  s.Steals,  rs.StealPoints));
            components.Add(Component("blocks",  s.Blocks,  rs.BlockPoints));
            components.Add(Component("saves",   s.Saves,   rs.SavePoints));
        }

        var value = components.Sum(c => c.Contribution);

        return new PlayerRating(inputs.PlayerId, "fantasy", value, components, rs.Name);
    }

    private static PlayerRatingComponent Component(string key, int count, double weight) =>
        new(key, count, weight, count * weight);
}
