using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ez.Handball.Tests.Infrastructure.Email;

public class ConsoleEmailSenderTests
{
    private readonly IEmailSender _sut = new ConsoleEmailSender(NullLogger<ConsoleEmailSender>.Instance);

    [Fact]
    public async Task SendVerificationEmailAsync_DoesNotThrow()
        => await _sut.SendVerificationEmailAsync("a@b.is", "http://localhost/verify?token=abc", "abc", default);

    [Fact]
    public async Task SendPasswordResetEmailAsync_DoesNotThrow()
        => await _sut.SendPasswordResetEmailAsync("a@b.is", "http://localhost/reset?token=abc", "abc", default);
}
