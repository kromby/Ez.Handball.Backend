using Ez.Handball.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.Email;

// Default sender outside Development: never logs the token-bearing link. A real provider
// (Azure Communication Services / SendGrid) is a deferred follow-up; until then non-dev
// environments get a safe no-op rather than leaking secrets to logs.
internal sealed class NoopEmailSender : IEmailSender
{
    private readonly ILogger<NoopEmailSender> _logger;

    public NoopEmailSender(ILogger<NoopEmailSender> logger) => _logger = logger;

    public Task SendVerificationEmailAsync(string email, string link, string token, CancellationToken ct)
    {
        _logger.LogWarning("Email sending is not configured; verification email for {Email} was not sent.", email);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string link, string token, CancellationToken ct)
    {
        _logger.LogWarning("Email sending is not configured; password-reset email for {Email} was not sent.", email);
        return Task.CompletedTask;
    }
}
