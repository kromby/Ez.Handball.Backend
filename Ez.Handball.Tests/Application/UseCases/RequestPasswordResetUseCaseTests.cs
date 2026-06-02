using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class RequestPasswordResetUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEmailTokenRepository> _emailTokens = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly AuthSettings _settings = new("http://localhost/verify?token={token}", "http://localhost/reset?token={token}");

    private RequestPasswordResetUseCase CreateSut() =>
        new(_users.Object, _emailTokens.Object, _tokens.Object, _email.Object, _settings);

    [Fact]
    public async Task ExistingEmail_CreatesResetToken_AndSendsLink()
    {
        _users.Setup(u => u.GetByEmailAsync("a@b.is", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", Email = "a@b.is" });
        _tokens.Setup(t => t.CreateEmailToken()).Returns(new IssuedToken("rvalue", "rhash", Now.AddHours(24)));

        var result = await CreateSut().ExecuteAsync("A@B.is", CancellationToken.None);

        Assert.IsType<RequestPasswordResetResult.Accepted>(result);
        _emailTokens.Verify(t => t.AddAsync(
            It.Is<EmailTokenEntity>(e => e.PartitionKey == "reset" && e.RowKey == "rhash" && e.UserId == "u-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendPasswordResetEmailAsync(
            "a@b.is", "http://localhost/reset?token=rvalue", "rvalue", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownEmail_SendsNothing_ButStillReturnsAccepted()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((UserEntity?)null);

        var result = await CreateSut().ExecuteAsync("ghost@x.is", CancellationToken.None);

        Assert.IsType<RequestPasswordResetResult.Accepted>(result);
        _emailTokens.Verify(t => t.AddAsync(It.IsAny<EmailTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
