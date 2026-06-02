using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class ResendVerificationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEmailTokenRepository> _emailTokens = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly AuthSettings _settings = new("http://localhost/verify?token={token}", "http://localhost/reset?token={token}");

    private ResendVerificationUseCase CreateSut() =>
        new(_users.Object, _emailTokens.Object, _tokens.Object, _email.Object, _settings);

    [Fact]
    public async Task Unverified_CreatesTokenAndSends()
    {
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", Email = "a@b.is", EmailVerified = false });
        _tokens.Setup(t => t.CreateEmailToken()).Returns(new IssuedToken("vvalue", "vhash", Now.AddHours(24)));

        var result = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        Assert.IsType<ResendVerificationResult.Accepted>(result);
        _emailTokens.Verify(t => t.AddAsync(
            It.Is<EmailTokenEntity>(e => e.PartitionKey == "verify" && e.RowKey == "vhash" && e.UserId == "u-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=vvalue", "vvalue", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlreadyVerified_SendsNothing_StillAccepted()
    {
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", Email = "a@b.is", EmailVerified = true });

        var result = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        Assert.IsType<ResendVerificationResult.Accepted>(result);
        _email.Verify(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingUser_ReturnsNotFound()
    {
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((UserEntity?)null);
        Assert.IsType<ResendVerificationResult.NotFound>(await CreateSut().ExecuteAsync("u-1", CancellationToken.None));
    }
}
