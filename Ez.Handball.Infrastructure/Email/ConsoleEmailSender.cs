using Ez.Handball.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.Email;

internal sealed class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public Task SendVerificationEmailAsync(string email, string link, string token, CancellationToken ct)
    {
        _logger.LogInformation("[DEV EMAIL] Verify {Email}: {Link}", email, link);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string link, string token, CancellationToken ct)
    {
        _logger.LogInformation("[DEV EMAIL] Password reset {Email}: {Link}", email, link);
        return Task.CompletedTask;
    }
}
