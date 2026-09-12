using System.Net;
using Ez.Handball.Infrastructure.Email;

namespace Ez.Handball.Tests.Infrastructure.Email;

public class EmailTemplatesTests
{
    [Theory]
    [InlineData("is")]
    [InlineData("en")]
    public void Verification_RendersLinkInSubjectFreeBody_NoLeftoverPlaceholders(string language)
    {
        var (subject, html, text) = EmailTemplates.Verification(language, "http://localhost/verify?token=abc");

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains("http://localhost/verify?token=abc", html);
        Assert.Contains("http://localhost/verify?token=abc", text);
        Assert.DoesNotContain("{link}", subject + html + text);
    }

    [Fact]
    public void Verification_UnknownLanguage_FallsBackToEnglish()
    {
        var (subject, _, _) = EmailTemplates.Verification("fr", "http://localhost/verify?token=abc");
        var (englishSubject, _, _) = EmailTemplates.Verification("en", "http://localhost/verify?token=abc");

        Assert.Equal(englishSubject, subject);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("en")]
    public void PasswordReset_RendersLink_NoLeftoverPlaceholders(string language)
    {
        var (subject, html, text) = EmailTemplates.PasswordReset(language, "http://localhost/reset?token=abc");

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains("http://localhost/reset?token=abc", html);
        Assert.Contains("http://localhost/reset?token=abc", text);
        Assert.DoesNotContain("{link}", subject + html + text);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("en")]
    public void MiniLeagueInvite_RendersInviterLeagueAndLink_NoLeftoverPlaceholders(string language)
    {
        var (subject, html, text) = EmailTemplates.MiniLeagueInvite(
            language, "Jón", "Office League", "http://localhost/join?token=abc");

        Assert.Contains("Jón", subject);
        Assert.Contains("Office League", subject);
        Assert.Contains("http://localhost/join?token=abc", html);
        Assert.Contains("http://localhost/join?token=abc", text);
        Assert.DoesNotContain("{link}", subject + html + text);
        Assert.DoesNotContain("{inviterName}", subject + html + text);
        Assert.DoesNotContain("{leagueName}", subject + html + text);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("en")]
    public void MiniLeagueInvite_MarkupInInviterOrLeagueName_IsEncodedInHtml_ButRawInText(string language)
    {
        const string maliciousInviter = "<script>alert(1)</script>";
        const string maliciousLeague = "Evil <img src=x onerror=alert(2)> League";

        var (_, html, text) = EmailTemplates.MiniLeagueInvite(
            language, maliciousInviter, maliciousLeague, "http://localhost/join?token=abc");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("<img src=x onerror=alert(2)>", html);
        Assert.Contains(WebUtility.HtmlEncode(maliciousInviter), html);
        Assert.Contains(WebUtility.HtmlEncode(maliciousLeague), html);

        // Text is a plain-text context: the raw values must appear verbatim, unescaped.
        Assert.Contains(maliciousInviter, text);
        Assert.Contains(maliciousLeague, text);
    }
}
