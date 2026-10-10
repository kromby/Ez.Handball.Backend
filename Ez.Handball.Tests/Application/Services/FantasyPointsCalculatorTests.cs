using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class FantasyPointsCalculatorTests
{
    private static readonly ScoringRuleSet Rules = new(GameFlavor.Fantasy, 2, GoalPoints: 2,
        YellowCardPoints: -1, TwoMinutePoints: -2, RedCardPoints: -5, AppearancePoints: 1, SavePoints: 0.5);

    private static FantasyPointsCalculator Sut() =>
        new(new FantasyPlayerRatingFunction(), new Mock<IScoringRuleSetRepository>().Object);

    [Theory]
    [InlineData(0, 1)] // appearance only
    [InlineData(1, 1)] // half a point rounds down
    [InlineData(2, 2)]
    [InlineData(7, 4)] // 1 + 3.5 → 4
    public void Saves_ScoreOnePointPerTwoSaves(int saves, double expected)
    {
        var stats = new AggregatedStats(Games: 1, Goals: 0, 0, 0, 0, Saves: saves);

        Assert.Equal(expected, Sut().Score("gk", stats, Rules));
    }

    [Fact]
    public void NegativeTotal_WithOddSaves_RoundsDownThePairsOnly()
    {
        // 1 appearance − 5 red card + 1.5 for 3 saves = −2.5 → −3 (one point for the one full pair).
        var stats = new AggregatedStats(Games: 1, Goals: 0, 0, 0, RedCards: 1, Saves: 3);

        Assert.Equal(-3, Sut().Score("gk", stats, Rules));
    }
}
