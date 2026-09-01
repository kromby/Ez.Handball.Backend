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
        Assert.Equal(2, fn.DefaultRuleSetVersion);
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
            c => { Assert.Equal("redCards", c.Key);    Assert.Equal(0, c.Value);  Assert.Equal(-5, c.Weight); Assert.Equal(0, c.Contribution); },
            c => { Assert.Equal("assists", c.Key);     Assert.Equal(0, c.Value);  Assert.Equal(0, c.Weight);  Assert.Equal(0, c.Contribution); },
            c => { Assert.Equal("steals", c.Key);      Assert.Equal(0, c.Value);  Assert.Equal(0, c.Weight);  Assert.Equal(0, c.Contribution); },
            c => { Assert.Equal("blocks", c.Key);      Assert.Equal(0, c.Value);  Assert.Equal(0, c.Weight);  Assert.Equal(0, c.Contribution); },
            c => { Assert.Equal("saves", c.Key);       Assert.Equal(0, c.Value);  Assert.Equal(0, c.Weight);  Assert.Equal(0, c.Contribution); });
    }

    [Fact]
    public void Compute_ZeroStats_ReturnsZero()
    {
        var stats = new AggregatedStats(0, 0, 0, 0, 0);

        var result = new FantasyPlayerRatingFunction().Compute(Inputs(stats));

        Assert.Equal(0, result.Rating);
        Assert.All(result.Components, c => Assert.Equal(0, c.Contribution));
    }

    [Fact]
    public void Compute_WithHbStatzComponents_IncludesThemInWeightedSum()
    {
        var rs = new ScoringRuleSet(GameFlavor.Fantasy, 2, GoalPoints: 2, YellowCardPoints: -1, TwoMinutePoints: -2,
            RedCardPoints: -5, AppearancePoints: 1, AssistPoints: 1, StealPoints: 1, BlockPoints: 1, SavePoints: 0.5);
        var stats = new AggregatedStats(Games: 1, Goals: 2, YellowCards: 0, TwoMinuteSuspensions: 0, RedCards: 0,
            Assists: 3, Steals: 2, Blocks: 1, Saves: 10);

        // 2*2 + 1*1 + 3*1 + 2*1 + 1*1 + 10*0.5 = 4 + 1 + 3 + 2 + 1 + 5 = 16
        var result = new FantasyPlayerRatingFunction().Compute(new PlayerRatingInputs("p1", stats, rs, Ctx()));

        Assert.Equal("fantasy-v2", result.Version);
        Assert.Equal(16, result.Rating);
        Assert.Contains(result.Components, c => c.Key == "assists" && c.Contribution == 3);
        Assert.Contains(result.Components, c => c.Key == "steals" && c.Contribution == 2);
        Assert.Contains(result.Components, c => c.Key == "blocks" && c.Contribution == 1);
        Assert.Contains(result.Components, c => c.Key == "saves" && c.Contribution == 5);
    }
}
