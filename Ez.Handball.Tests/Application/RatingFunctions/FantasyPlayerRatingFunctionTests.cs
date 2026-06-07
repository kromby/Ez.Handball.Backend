using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.RatingFunctions;

public class FantasyPlayerRatingFunctionTests
{
    private static readonly ScoringRuleSet RuleSet =
        new(GameFlavor.Fantasy, 1,
            GoalPoints: 2, YellowCardPoints: -1, TwoMinutePoints: -2,
            RedCardPoints: -5, AppearancePoints: 1);

    private static PlayerRatingContext Ctx() => new(null, null, null, null, null, null);

    private static PlayerRatingInputs Inputs(AggregatedStats stats) =>
        new("p1", stats, RuleSet, Ctx());

    [Fact]
    public void Flavor_And_DefaultRuleSetVersion()
    {
        var fn = new FantasyPlayerRatingFunction();

        Assert.Equal(GameFlavor.Fantasy, fn.Flavor);
        Assert.Equal(1, fn.DefaultRuleSetVersion);
    }

    [Fact]
    public void Compute_WeightedSum_AndComponents()
    {
        // 18 goals, 9 games, 4 yellow, 2 two-min, 0 red
        // 18*2 + 9*1 + 4*-1 + 2*-2 + 0*-5 = 36 + 9 - 4 - 4 + 0 = 37
        var stats = new AggregatedStats(Games: 9, Goals: 18, YellowCards: 4, TwoMinuteSuspensions: 2, RedCards: 0);

        var result = new FantasyPlayerRatingFunction().Compute(Inputs(stats));

        Assert.Equal("p1", result.PlayerId);
        Assert.Equal("fantasy", result.Flavor);
        Assert.Equal("fantasy-v1", result.Version);
        Assert.Equal(37, result.Rating);

        Assert.Collection(result.Components,
            c => { Assert.Equal("goals", c.Key);       Assert.Equal(18, c.Value); Assert.Equal(2, c.Weight);  Assert.Equal(36, c.Contribution); },
            c => { Assert.Equal("appearances", c.Key); Assert.Equal(9, c.Value);  Assert.Equal(1, c.Weight);  Assert.Equal(9, c.Contribution); },
            c => { Assert.Equal("yellowCards", c.Key); Assert.Equal(4, c.Value);  Assert.Equal(-1, c.Weight); Assert.Equal(-4, c.Contribution); },
            c => { Assert.Equal("twoMinute", c.Key);   Assert.Equal(2, c.Value);  Assert.Equal(-2, c.Weight); Assert.Equal(-4, c.Contribution); },
            c => { Assert.Equal("redCards", c.Key);    Assert.Equal(0, c.Value);  Assert.Equal(-5, c.Weight); Assert.Equal(0, c.Contribution); });
    }

    [Fact]
    public void Compute_ZeroStats_ReturnsZero()
    {
        var stats = new AggregatedStats(0, 0, 0, 0, 0);

        var result = new FantasyPlayerRatingFunction().Compute(Inputs(stats));

        Assert.Equal(0, result.Rating);
        Assert.All(result.Components, c => Assert.Equal(0, c.Contribution));
    }
}
