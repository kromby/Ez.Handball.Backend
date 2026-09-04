using Ez.Handball.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.Email;

internal sealed class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, _, text) = EmailTemplates.Verification(language, link);
        Log(email, subject, text);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, _, text) = EmailTemplates.PasswordReset(language, link);
        Log(email, subject, text);
        return Task.CompletedTask;
    }

    public Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct)
    {
        var (subject, _, text) = EmailTemplates.MiniLeagueInvite(language, inviterName, leagueName, link);
        Log(email, subject, text);
        return Task.CompletedTask;
    }

    private void Log(string email, string subject, string text)
        => _logger.LogInformation("[DEV EMAIL] To: {Email}\nSubject: {Subject}\n{Body}", email, subject, text);
}
