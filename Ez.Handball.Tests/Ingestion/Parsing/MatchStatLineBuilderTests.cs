using Ez.Handball.Ingestion.Parsing;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class MatchStatLineBuilderTests
{
    private static string LoadFixture() =>
        System.IO.File.ReadAllText("../../../../tools/HbStatz.Spike.Tests/fixtures/pergame/12922-home.html");

    [Fact]
    public void Build_MapsBothOutfieldAndGoalkeepersCorrectly()
    {
        var tables = StatsTableParser.ParseAll(LoadFixture());
        var lines = MatchStatLineBuilder.Build(tables, "home");

        Assert.NotEmpty(lines);
        var gk = lines.First(l => l.IsGoalkeeper);
        Assert.NotNull(gk.Saves);
        Assert.NotNull(gk.SaveRate);

        var outfield = lines.First(l => !l.IsGoalkeeper && l.Goals > 0);
        Assert.NotNull(outfield.ExpectedGoals);
        Assert.NotNull(outfield.PlusMinus);
        Assert.NotNull(outfield.Assists);
    }
}
