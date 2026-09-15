using Azure;
using Azure.Communication.Email;
using Ez.Handball.Infrastructure.Email;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Email;

public class AcsEmailSenderTests
{
    private readonly Mock<EmailClient> _client = new();

    private AcsEmailSender CreateSut()
    {
        _client.Setup(c => c.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new EmailSendOperation("op-1", _client.Object));
        return new AcsEmailSender(_client.Object, "no-reply@handbolti.is");
    }

    [Fact]
    public async Task SendVerificationEmailAsync_SendsRenderedTemplate_ToCorrectRecipient()
    {
        await CreateSut().SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=abc", "is", CancellationToken.None);

        _client.Verify(c => c.SendAsync(
            WaitUntil.Started,
            It.Is<EmailMessage>(m =>
                m.SenderAddress == "no-reply@handbolti.is" &&
                m.Recipients.To.Single().Address == "a@b.is" &&
                m.Content.Subject == "Staðfestu netfangið þitt hjá Olís deildin - Fantasy" &&
                m.Content.PlainText!.Contains("http://localhost/verify?token=abc")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_SendsRenderedTemplate_InRequestedLanguage()
    {
        await CreateSut().SendPasswordResetEmailAsync(
            "a@b.is", "http://localhost/reset?token=abc", "en", CancellationToken.None);

        _client.Verify(c => c.SendAsync(
            WaitUntil.Started,
            It.Is<EmailMessage>(m => m.Content.Subject == "Reset your password for Olís deildin - Fantasy"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMiniLeagueInviteEmailAsync_IncludesInviterAndLeagueName()
    {
        await CreateSut().SendMiniLeagueInviteEmailAsync(
            "a@b.is", "Jón", "Office League", "http://localhost/join?token=abc", "en", CancellationToken.None);

        _client.Verify(c => c.SendAsync(
            WaitUntil.Started,
            It.Is<EmailMessage>(m =>
                m.Content.Subject == "Jón invited you to \"Office League\" on Olís deildin - Fantasy"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
