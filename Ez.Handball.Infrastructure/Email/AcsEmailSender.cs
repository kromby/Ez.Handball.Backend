using Azure;
using Azure.Communication.Email;
using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Infrastructure.Email;

internal sealed class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly string _fromAddress;

    public AcsEmailSender(EmailClient client, string fromAddress)
    {
        _client = client;
        _fromAddress = fromAddress;
    }

    public Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, html, text) = EmailTemplates.Verification(language, link);
        return SendAsync(email, subject, html, text, ct);
    }

    public Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, html, text) = EmailTemplates.PasswordReset(language, link);
        return SendAsync(email, subject, html, text, ct);
    }

    public Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct)
    {
        var (subject, html, text) = EmailTemplates.MiniLeagueInvite(language, inviterName, leagueName, link);
        return SendAsync(email, subject, html, text, ct);
    }

    // WaitUntil.Started: queue the send with ACS and return immediately rather than polling for
    // final delivery status — transactional callers shouldn't block the HTTP response on ACS's
    // full send pipeline.
    private async Task SendAsync(string toAddress, string subject, string html, string text, CancellationToken ct)
    {
        var content = new EmailContent(subject) { Html = html, PlainText = text };
        var message = new EmailMessage(_fromAddress, toAddress, content);
        await _client.SendAsync(WaitUntil.Started, message, ct);
    }
}
