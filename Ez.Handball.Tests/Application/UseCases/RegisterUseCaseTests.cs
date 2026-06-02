using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class RegisterUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IEmailTokenRepository> _emailTokens = new();
    private readonly Mock<IClubRepository> _clubs = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly AuthSettings _settings = new("http://localhost/verify?token={token}", "http://localhost/reset?token={token}");

    private RegisterUseCase CreateSut() => new(
        _users.Object, _refresh.Object, _emailTokens.Object, _clubs.Object,
        _hasher.Object, _tokens.Object, _email.Object, _settings, () => Now);

    private static RegisterCommand ValidCmd() =>
        new("A@B.is", "hunter2hunter2", "Jón", "is", "385");

    private void HappyPathStubs()
    {
        _clubs.Setup(c => c.ExistsAsync("385", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.TryReserveEmailAsync("a@b.is", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _hasher.Setup(h => h.Hash("hunter2hunter2")).Returns("HASHED");
        _tokens.Setup(t => t.CreateEmailToken()).Returns(new IssuedToken("evalue", "ehash", Now.AddHours(24)));
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<UserEntity>())).Returns("ACCESS");
        _tokens.Setup(t => t.CreateRefreshToken(It.IsAny<string>())).Returns(new IssuedToken("rvalue", "rhash", Now.AddDays(30)));
        _tokens.Setup(t => t.AccessTokenSeconds).Returns(900);
    }

    [Fact]
    public async Task HappyPath_NormalizesEmail_WritesUserIndexTokenEmail_AndAutoLogsIn()
    {
        HappyPathStubs();
        UserEntity? written = null;
        _users.Setup(u => u.AddAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()))
              .Callback<UserEntity, CancellationToken>((u, _) => written = u);

        var result = await CreateSut().ExecuteAsync(ValidCmd(), CancellationToken.None);

        var success = Assert.IsType<RegisterResult.Success>(result);
        Assert.Equal("ACCESS", success.AccessToken);
        Assert.Equal("rvalue", success.RefreshToken);
        Assert.Equal(900, success.ExpiresIn);

        Assert.NotNull(written);
        Assert.Equal("a@b.is", written!.Email);
        Assert.Equal("HASHED", written.PasswordHash);
        Assert.False(written.EmailVerified);
        Assert.Equal(Now, written.CreatedAt);

        _emailTokens.Verify(t => t.AddAsync(
            It.Is<EmailTokenEntity>(e => e.PartitionKey == "verify" && e.RowKey == "ehash"),
            It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=evalue", "evalue", It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(r => r.AddAsync(
            It.Is<RefreshTokenEntity>(t => t.RowKey == "rhash"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DuplicateEmail_ReturnsEmailTaken_AndDoesNotWriteUser()
    {
        _clubs.Setup(c => c.ExistsAsync("385", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.TryReserveEmailAsync("a@b.is", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync(ValidCmd(), CancellationToken.None);

        Assert.IsType<RegisterResult.EmailTaken>(result);
        _users.Verify(u => u.AddAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownClub_ReturnsInvalidClub()
    {
        _clubs.Setup(c => c.ExistsAsync("385", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync(ValidCmd(), CancellationToken.None);

        Assert.IsType<RegisterResult.InvalidClub>(result);
        _users.Verify(u => u.TryReserveEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WeakPassword_ReturnsWeakPassword()
    {
        var result = await CreateSut().ExecuteAsync(ValidCmd() with { Password = "short" }, CancellationToken.None);
        Assert.IsType<RegisterResult.WeakPassword>(result);
    }

    [Fact]
    public async Task BadEmail_ReturnsValidationError()
    {
        var result = await CreateSut().ExecuteAsync(ValidCmd() with { Email = "no-at" }, CancellationToken.None);
        var err = Assert.IsType<RegisterResult.ValidationError>(result);
        Assert.Equal("email", err.Field);
    }
}
