using Ez.Handball.Ingestion.Parsing;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Parsing;

public class PositionMergeTests
{
    [Theory]
    [InlineData("CB", "LB", "LB", null, "LB", "CB")]   // data moves the primary; old primary becomes secondary
    [InlineData("CB", "LB", "CB", null, "CB", "LB")]   // no observed secondary: admin secondary survives
    [InlineData("CB", "LB", "RB", "LW", "RB", "LW")]   // data corrects both positions
    [InlineData("CB", "", "CB", null, "CB", "")]       // nothing to keep
    [InlineData("", "", "CB", null, "CB", "")]         // no existing positions
    [InlineData("CB", "", "LB", null, "LB", "CB")]     // old primary moves to secondary
    [InlineData("Leikmaður", "", "CB", null, "CB", "")]     // hsi.is placeholder is never a position
    [InlineData("CB", "Leikmaður", "CB", null, "CB", "")]   // nor as a stored secondary
    [InlineData("Leikmaður", "LB", "CB", null, "CB", "LB")] // placeholder skipped, real secondary kept
    [InlineData(null, null, "CB", null, "CB", "")]          // null existing values
    [InlineData("CB", "LB", "CB", "  ", "CB", "LB")]        // whitespace computed secondary is none
    public void Apply_MatchesRule(
        string? existingPrimary, string? existingSecondary,
        string computedPrimary, string? computedSecondary,
        string expectedPrimary, string expectedSecondary)
    {
        var (primary, secondary) = PositionMerge.Apply(existingPrimary, existingSecondary, computedPrimary, computedSecondary);

        Assert.Equal(expectedPrimary, primary);
        Assert.Equal(expectedSecondary, secondary);
    }

    [Fact]
    public void Apply_ComputedSecondaryEqualToComputedPrimary_TreatedAsNone()
    {
        var (primary, secondary) = PositionMerge.Apply("CB", "LB", "CB", "CB");

        Assert.Equal("CB", primary);
        Assert.Equal("LB", secondary);
    }

    [Fact]
    public void Apply_ExistingSecondaryEqualToNewPrimary_WithBlankExistingPrimary_ReturnsBlank()
    {
        var (primary, secondary) = PositionMerge.Apply("", "CB", "CB", null);

        Assert.Equal("CB", primary);
        Assert.Equal("", secondary);
    }

    [Fact]
    public void Apply_WhitespaceExistingValues_TreatedAsBlank()
    {
        var (primary, secondary) = PositionMerge.Apply(" ", "  ", "CB", null);

        Assert.Equal("CB", primary);
        Assert.Equal("", secondary);
    }

    [Fact]
    public void Apply_WhitespaceExistingSecondary_FallsThroughToExistingPrimary()
    {
        var (primary, secondary) = PositionMerge.Apply("CB", " ", "LB", null);

        Assert.Equal("LB", primary);
        Assert.Equal("CB", secondary);
    }
}
