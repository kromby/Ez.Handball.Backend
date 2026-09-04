namespace Ez.Handball.Infrastructure.Email;

internal static class EmailTemplates
{
    private const string Brand = "Olís deildin - Fantasy";

    public static (string Subject, string Html, string Text) Verification(string language, string link)
        => language == "is"
            ? (
                $"Staðfestu netfangið þitt hjá {Brand}",
                $"<p>Til að staðfesta netfangið þitt, smelltu á hlekkinn hér að neðan:</p>" +
                $"<p><a href=\"{link}\">Staðfesta netfang</a></p>" +
                $"<p>Ef hlekkurinn virkar ekki, afritaðu þessa slóð í vafrann þinn:<br>{link}</p>",
                $"Til að staðfesta netfangið þitt, farðu á eftirfarandi hlekk:\n{link}"
              )
            : (
                $"Verify your email for {Brand}",
                $"<p>To verify your email address, click the link below:</p>" +
                $"<p><a href=\"{link}\">Verify email</a></p>" +
                $"<p>If the link doesn't work, copy this URL into your browser:<br>{link}</p>",
                $"To verify your email address, follow this link:\n{link}"
              );

    public static (string Subject, string Html, string Text) PasswordReset(string language, string link)
        => language == "is"
            ? (
                $"Endurstilltu lykilorðið þitt hjá {Brand}",
                $"<p>Þú baðst um að endurstilla lykilorðið þitt. Smelltu á hlekkinn hér að neðan til að velja nýtt lykilorð:</p>" +
                $"<p><a href=\"{link}\">Endurstilla lykilorð</a></p>" +
                $"<p>Ef þú baðst ekki um þetta geturðu hunsað þennan póst.</p>",
                $"Þú baðst um að endurstilla lykilorðið þitt. Farðu á eftirfarandi hlekk til að velja nýtt lykilorð:\n{link}\n\n" +
                $"Ef þú baðst ekki um þetta geturðu hunsað þennan póst."
              )
            : (
                $"Reset your password for {Brand}",
                $"<p>You asked to reset your password. Click the link below to choose a new one:</p>" +
                $"<p><a href=\"{link}\">Reset password</a></p>" +
                $"<p>If you didn't request this, you can ignore this email.</p>",
                $"You asked to reset your password. Follow this link to choose a new one:\n{link}\n\n" +
                $"If you didn't request this, you can ignore this email."
              );

    public static (string Subject, string Html, string Text) MiniLeagueInvite(
        string language, string inviterName, string leagueName, string link)
        => language == "is"
            ? (
                $"{inviterName} bauð þér í deildina \"{leagueName}\" hjá {Brand}",
                $"<p>{inviterName} bauð þér að ganga í deildina <strong>{leagueName}</strong> hjá {Brand}.</p>" +
                $"<p><a href=\"{link}\">Skrá mig í deildina</a></p>" +
                $"<p>Ef hlekkurinn virkar ekki, afritaðu þessa slóð í vafrann þinn:<br>{link}</p>",
                $"{inviterName} bauð þér að ganga í deildina \"{leagueName}\" hjá {Brand}.\n\nGakktu í deildina hér:\n{link}"
              )
            : (
                $"{inviterName} invited you to \"{leagueName}\" on {Brand}",
                $"<p>{inviterName} invited you to join <strong>{leagueName}</strong> on {Brand}.</p>" +
                $"<p><a href=\"{link}\">Join the league</a></p>" +
                $"<p>If the link doesn't work, copy this URL into your browser:<br>{link}</p>",
                $"{inviterName} invited you to join \"{leagueName}\" on {Brand}.\n\nJoin here:\n{link}"
              );
}
