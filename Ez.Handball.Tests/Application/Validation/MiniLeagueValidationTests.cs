using Ez.Handball.Application.Validation;

namespace Ez.Handball.Tests.Application.Validation;

public class MiniLeagueValidationTests
{
    [Theory]
    [InlineData("Office League")]
    [InlineData("A")]
    public void IsValidName_AcceptsNonEmptyWithinLength(string name)
        => Assert.True(MiniLeagueValidation.IsValidName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidName_RejectsBlank(string? name)
        => Assert.False(MiniLeagueValidation.IsValidName(name!));

    [Fact]
    public void IsValidName_Accepts60Chars()
        => Assert.True(MiniLeagueValidation.IsValidName(new string('x', 60)));

    [Fact]
    public void IsValidName_Rejects61Chars()
        => Assert.False(MiniLeagueValidation.IsValidName(new string('x', 61)));
}
