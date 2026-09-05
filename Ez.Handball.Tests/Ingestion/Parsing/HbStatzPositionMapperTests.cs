using Ez.Handball.Ingestion.Parsing;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class HbStatzPositionMapperTests
{
    [Theory]
    [InlineData("Goalkeeper", "GK")]
    [InlineData("Left Wing", "LW")]
    [InlineData("Right Wing", "RW")]
    [InlineData("Left Back", "LB")]
    [InlineData("Right Back", "RB")]
    [InlineData("Center", "CB")]
    [InlineData("Line", "LP")]
    [InlineData("goalkeeper", "GK")] // case-insensitive
    [InlineData(" Left Wing ", "LW")] // trims whitespace
    public void MapToCode_KnownLabel_ReturnsExpectedCode(string label, string expected)
    {
        Assert.Equal(expected, HbStatzPositionMapper.MapToCode(label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Pivot")] // not an HBStatz label we've seen
    public void MapToCode_UnrecognizedOrBlank_ReturnsNull(string? label)
    {
        Assert.Null(HbStatzPositionMapper.MapToCode(label));
    }

    [Fact]
    public void PositionVocabulary_ContainsExactlySevenCodes()
    {
        Assert.Equal(
            new[] { "CB", "GK", "LB", "LP", "LW", "RB", "RW" },
            PositionVocabulary.Codes.OrderBy(c => c));
    }
}
