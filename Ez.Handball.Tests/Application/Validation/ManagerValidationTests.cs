using Ez.Handball.Application.Validation;

namespace Ez.Handball.Tests.Application.Validation;

public class ManagerValidationTests
{
    [Theory]
    [InlineData("  Dream Team  ", "dream team")]
    [InlineData("FC Awesome", "fc awesome")]
    public void Normalize_TrimsAndLowercases(string input, string expected)
        => Assert.Equal(expected, ManagerValidation.NormalizeTeamName(input));

    [Fact]
    public void IsAllowed_CleanName_True()
        => Assert.True(ManagerValidation.IsAllowedTeamName("Dream Team"));

    [Theory]
    [InlineData("admin")]            // reserved word
    [InlineData("ADMIN")]            // case-insensitive
    [InlineData("the fuck team")]    // contains a blocked term
    public void IsAllowed_BlockedName_False(string name)
        => Assert.False(ManagerValidation.IsAllowedTeamName(name));
}
