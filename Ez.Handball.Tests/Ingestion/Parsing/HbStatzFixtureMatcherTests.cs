using Ez.Handball.Ingestion.Models;
using Ez.Handball.Ingestion.Parsing;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class HbStatzFixtureMatcherTests
{
    private static HbStatzFixture Fixture(
        int gameId, string date, string home, string away, bool hasHbs = true) => new()
    {
        GameId = gameId,
        Date = date,
        Home = new HbStatzFixtureTeam { Name = home },
        Away = new HbStatzFixtureTeam { Name = away },
        Played = true,
        HasHbs = hasHbs
    };

    [Fact]
    public void FindMatch_SameDayAndTeams_ReturnsFixture()
    {
        var fixtures = new[] { Fixture(12924, "2026-05-07 18:29:48", "Valur", "FH") };

        var result = HbStatzFixtureMatcher.FindMatch(
            fixtures, new DateTimeOffset(2026, 5, 7, 20, 0, 0, TimeSpan.Zero), "Valur", "FH");

        Assert.NotNull(result);
        Assert.Equal(12924, result!.GameId);
    }

    [Fact]
    public void FindMatch_IsCaseInsensitiveAndTrimsWhitespace()
    {
        var fixtures = new[] { Fixture(1, "2026-05-07 18:29:48", " valur ", "FH") };

        var result = HbStatzFixtureMatcher.FindMatch(
            fixtures, new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero), "Valur", "fh");

        Assert.NotNull(result);
    }

    [Fact]
    public void FindMatch_DifferentDay_ReturnsNull()
    {
        var fixtures = new[] { Fixture(1, "2026-05-08 18:29:48", "Valur", "FH") };

        var result = HbStatzFixtureMatcher.FindMatch(
            fixtures, new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero), "Valur", "FH");

        Assert.Null(result);
    }

    [Fact]
    public void FindMatch_TeamsSwapped_ReturnsNull()
    {
        var fixtures = new[] { Fixture(1, "2026-05-07 18:29:48", "Valur", "FH") };

        var result = HbStatzFixtureMatcher.FindMatch(
            fixtures, new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero), "FH", "Valur");

        Assert.Null(result);
    }

    [Fact]
    public void FindMatch_HasHbsFalse_IsIgnored()
    {
        var fixtures = new[] { Fixture(1, "2026-05-07 18:29:48", "Valur", "FH", hasHbs: false) };

        var result = HbStatzFixtureMatcher.FindMatch(
            fixtures, new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero), "Valur", "FH");

        Assert.Null(result);
    }
}
