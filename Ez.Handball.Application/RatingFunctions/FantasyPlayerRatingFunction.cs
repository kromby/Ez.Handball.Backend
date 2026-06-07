using Ez.Handball.Domain;

namespace Ez.Handball.Application.RatingFunctions;

public sealed class FantasyPlayerRatingFunction : IPlayerRatingFunction
{
    public GameFlavor Flavor => GameFlavor.Fantasy;
    public int? DefaultRuleSetVersion => 1;

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

        var value = components.Sum(c => c.Contribution);

        return new PlayerRating(inputs.PlayerId, "fantasy", value, components, rs.Name);
    }

    private static PlayerRatingComponent Component(string key, int count, double weight) =>
        new(key, count, weight, count * weight);
}
