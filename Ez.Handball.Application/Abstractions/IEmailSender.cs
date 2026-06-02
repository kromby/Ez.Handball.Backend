namespace Ez.Handball.Application.Abstractions;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(string email, string link, string token, CancellationToken ct);
    Task SendPasswordResetEmailAsync(string email, string link, string token, CancellationToken ct);
}
