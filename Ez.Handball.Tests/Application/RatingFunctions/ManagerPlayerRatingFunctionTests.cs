using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Application.RatingFunctions;

public class ManagerPlayerRatingFunctionTests
{
    private static PlayerRatingInputs Inputs(AggregatedStats stats) =>
        new("p1", stats, RuleSet: null, new PlayerRatingContext(null, null, null, null, null));

    [Fact]
    public void Flavor_And_DefaultRuleSetVersion()
    {
        var fn = new ManagerPlayerRatingFunction();

        Assert.Equal(GameFlavor.Manager, fn.Flavor);
        Assert.Null(fn.DefaultRuleSetVersion);
    }

    [Fact]
    public void Compute_ReturnsDeterministicRatingAndMarketValue()
    {
        // rating = goals + games = 18 + 9 = 27; marketValue = 27 * 1000 = 27000
        var stats = new AggregatedStats(Games: 9, Goals: 18, YellowCards: 4, TwoMinuteSuspensions: 2, RedCards: 0);

        var result = new ManagerPlayerRatingFunction().Compute(Inputs(stats));

        Assert.Equal("p1", result.PlayerId);
        Assert.Equal("manager", result.Flavor);
        Assert.Equal("manager-v0", result.Version);
        Assert.Equal(27, result.Rating);

        var market = Assert.Single(result.Components);
        Assert.Equal("marketValue", market.Key);
        Assert.Equal(27000, market.Value);
    }

    [Fact]
    public void Compute_IgnoresNullRuleSet_DoesNotThrow()
    {
        var result = new ManagerPlayerRatingFunction()
            .Compute(Inputs(new AggregatedStats(0, 0, 0, 0, 0)));

        Assert.Equal(0, result.Rating);
    }
}
