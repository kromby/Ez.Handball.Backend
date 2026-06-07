using Ez.Handball.Domain;

namespace Ez.Handball.Tests.Domain;

public class TournamentTypeTests
{
    [Theory]
    [InlineData(TournamentType.League, "league")]
    [InlineData(TournamentType.Playoffs, "playoffs")]
    [InlineData(TournamentType.Cup, "cup")]
    public void ToWireString_ReturnsLowercaseCanonicalForm(TournamentType type, string expected)
    {
        Assert.Equal(expected, type.ToWireString());
    }

    [Theory]
    [InlineData("league", TournamentType.League)]
    [InlineData("LEAGUE", TournamentType.League)]
    [InlineData(" playoffs ", TournamentType.Playoffs)]
    [InlineData("Cup", TournamentType.Cup)]
    public void TryParse_AcceptsKnownValues_CaseInsensitiveAndTrimmed(string input, TournamentType expected)
    {
        Assert.True(TournamentTypes.TryParse(input, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("regular_season")]
    [InlineData("relegation_playoff")]
    [InlineData("bogus")]
    public void TryParse_RejectsUnknownOrBlank(string? input)
    {
        Assert.False(TournamentTypes.TryParse(input, out _));
    }
}
