using Ez.Handball.Application.ValueFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.ValueFunctions;

public class ManagerPlayerValueFunctionTests
{
    private static PlayerValueInputs Inputs(AggregatedStats stats) =>
        new("p1", stats, RuleSet: null, new PlayerValueContext(null, null, null, null, null, null));

    [Fact]
    public void Flavor_And_DefaultRuleSetVersion()
    {
        var fn = new ManagerPlayerValueFunction();

        Assert.Equal(ValueFlavor.Manager, fn.Flavor);
        Assert.Null(fn.DefaultRuleSetVersion);
    }

    [Fact]
    public void Compute_ReturnsDeterministicRatingAndMarketValue()
    {
        // rating = goals + games = 18 + 9 = 27; marketValue = 27 * 1000 = 27000
        var stats = new AggregatedStats(Games: 9, Goals: 18, YellowCards: 4, TwoMinuteSuspensions: 2, RedCards: 0);

        var result = new ManagerPlayerValueFunction().Compute(Inputs(stats));

        Assert.Equal("p1", result.PlayerId);
        Assert.Equal("manager", result.Flavor);
        Assert.Equal("manager-v0", result.Version);
        Assert.Equal(27, result.Value);

        var market = Assert.Single(result.Components);
        Assert.Equal("marketValue", market.Key);
        Assert.Equal(27000, market.Value);
    }

    [Fact]
    public void Compute_IgnoresNullRuleSet_DoesNotThrow()
    {
        var result = new ManagerPlayerValueFunction()
            .Compute(Inputs(new AggregatedStats(0, 0, 0, 0, 0)));

        Assert.Equal(0, result.Value);
    }
}
