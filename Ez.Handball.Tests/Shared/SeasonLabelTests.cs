using Ez.Handball.Shared;
using Xunit;

namespace Ez.Handball.Tests.Shared;

public class SeasonLabelTests
{
    [Theory]
    [InlineData(2025, "2025-26")]
    [InlineData(2024, "2024-25")]
    [InlineData(2009, "2009-10")] // decade rollover, two-digit zero-pad
    [InlineData(1999, "1999-00")] // century rollover
    public void Format_BuildsDashLabelFromStartYear(int startYear, string expected)
    {
        Assert.Equal(expected, SeasonLabel.Format(startYear));
    }

    [Fact]
    public void Resolve_ParsesIntegerSeasonParam()
    {
        Assert.Equal("2025-26", SeasonLabel.Resolve("2025", fallbackYear: 1900));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-year")]
    [InlineData("2025-26")] // already-formatted label is not an integer -> fallback
    public void Resolve_FallsBackToFallbackYearWhenParamNotInteger(string? param)
    {
        Assert.Equal("2030-31", SeasonLabel.Resolve(param, fallbackYear: 2030));
    }
}
