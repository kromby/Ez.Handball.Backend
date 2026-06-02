using Ez.Handball.Application.Validation;

namespace Ez.Handball.Tests.Application.Validation;

public class AuthValidationTests
{
    [Theory]
    [InlineData("  A@B.IS ", "a@b.is")]
    [InlineData("user@Example.Com", "user@example.com")]
    public void NormalizeEmail_TrimsAndLowercases(string input, string expected)
        => Assert.Equal(expected, AuthValidation.NormalizeEmail(input));

    [Theory]
    [InlineData("a@b.is", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("a@b", false)]
    [InlineData("", false)]
    public void IsValidEmail_ChecksShape(string email, bool expected)
        => Assert.Equal(expected, AuthValidation.IsValidEmail(email));

    [Theory]
    [InlineData("hunter2hunter2", true)]
    [InlineData("short", false)]
    public void IsValidPassword_EnforcesLength(string pw, bool expected)
        => Assert.Equal(expected, AuthValidation.IsValidPassword(pw));

    [Fact]
    public void IsValidPassword_RejectsOver128()
        => Assert.False(AuthValidation.IsValidPassword(new string('x', 129)));

    [Theory]
    [InlineData("Jón", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidDisplayName_RequiresNonEmpty(string name, bool expected)
        => Assert.Equal(expected, AuthValidation.IsValidDisplayName(name));

    [Fact]
    public void IsValidDisplayName_RejectsOver60()
        => Assert.False(AuthValidation.IsValidDisplayName(new string('x', 61)));

    [Theory]
    [InlineData("is", true)]
    [InlineData("en", true)]
    [InlineData("de", false)]
    public void IsValidLanguage_AllowsIsAndEn(string lang, bool expected)
        => Assert.Equal(expected, AuthValidation.IsValidLanguage(lang));
}
