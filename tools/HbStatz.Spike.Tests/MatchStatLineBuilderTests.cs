using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class MatchStatLineBuilderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pergame", name));

    private static IReadOnlyList<PlayerStatLine> HomeLines() =>
        MatchStatLineBuilder.Build(StatsTableParser.ParseAll(Fixture("12922-home.html")), "home");

    [Fact]
    public void Build_JoinsOutfieldGoalsAndCardsByPlayer()
    {
        var omar = HomeLines().Single(p => p.Name == "Ómar Darri Sigurgeirsson");

        Assert.Equal(25, omar.Jersey);
        Assert.False(omar.IsGoalkeeper);
        Assert.Equal(11, omar.Goals);                  // from offensive table
        Assert.Equal(0, omar.YellowCards);             // from discipline table
        Assert.Equal(1, omar.TwoMinuteSuspensions);    // from discipline table
        Assert.Equal(0, omar.RedCards);
        Assert.Equal("home", omar.Side);
    }

    [Fact]
    public void Build_EmitsGoalkeeperLinesFromGkTable()
    {
        var gk = HomeLines().Single(p => p.Name == "Daníel Freyr Andrésson");

        Assert.True(gk.IsGoalkeeper);
        Assert.Equal(1, gk.Jersey);
        Assert.Equal(0, gk.Goals);
        Assert.Equal(0, gk.YellowCards);
    }

    [Fact]
    public void Build_ReturnsAllPlayers_GkPlusOutfield()
    {
        // 12922 home: 2 GK + 13 outfield = 15 lines
        Assert.Equal(15, HomeLines().Count);
    }
}
