using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class FantasyScorerTests
{
    private static PlayerStatLine Line(int goals, int yellow, int two, int red, bool gk = false) =>
        new("home", 1, "Test Player", gk, goals, yellow, two, red);

    [Fact]
    public void Score_GoalsPlusAppearance()
    {
        // 9 goals + appearance = 2*9 + 1 = 19
        Assert.Equal(19, FantasyScorer.Score(Line(9, 0, 0, 0)));
    }

    [Fact]
    public void Score_YellowAndTwoMinuteAreNegative()
    {
        // 0 goals, 1 yellow, 1 two-min, appeared = -1 - 2 + 1 = -2
        Assert.Equal(-2, FantasyScorer.Score(Line(0, 1, 1, 0)));
    }

    [Fact]
    public void Score_RealOutfieldExample_OmarDarri()
    {
        // 12922 home: 11 goals, 0 yellow, 1 two-min, 0 red = 2*11 - 2 + 1 = 21
        Assert.Equal(21, FantasyScorer.Score(Line(11, 0, 1, 0)));
    }
}
