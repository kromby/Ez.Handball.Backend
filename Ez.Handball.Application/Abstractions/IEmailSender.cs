namespace Ez.Handball.Application.Abstractions;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct);
    Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct);
    Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct);
}
