using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class ResetPasswordUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEmailTokenRepository> _emailTokens = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();

    private ResetPasswordUseCase CreateSut() =>
        new(_users.Object, _emailTokens.Object, _refresh.Object, _hasher.Object, _tokens.Object, () => Now);

    private delegate bool HashCallback(string presented, out string hash);

    private void HashesTo(string presented, string hash) =>
        _tokens.Setup(t => t.TryHashEmailToken(presented, out It.Ref<string>.IsAny))
               .Returns(new HashCallback((string p, out string h) => { h = hash; return true; }));

    [Fact]
    public async Task ValidToken_SetsNewHash_DeletesToken_RevokesAllSessions()
    {
        HashesTo("tok", "thash");
        _emailTokens.Setup(t => t.GetAsync("reset", "thash", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new EmailTokenEntity { PartitionKey = "reset", RowKey = "thash", UserId = "u-1", ExpiresAt = Now.AddHours(1) });
        var user = new UserEntity { RowKey = "u-1", PasswordHash = "OLD" };
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.Hash("newpassword1")).Returns("NEWHASH");

        var result = await CreateSut().ExecuteAsync(new ResetPasswordCommand("tok", "newpassword1"), CancellationToken.None);

        Assert.IsType<ResetPasswordResult.Success>(result);
        Assert.Equal("NEWHASH", user.PasswordHash);
        Assert.Equal(Now, user.ChangedAt);
        _emailTokens.Verify(t => t.DeleteAsync("reset", "thash", It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(r => r.DeleteAllForUserAsync("u-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WeakNewPassword_ReturnsWeakPassword_BeforeTouchingToken()
    {
        var result = await CreateSut().ExecuteAsync(new ResetPasswordCommand("tok", "short"), CancellationToken.None);

        Assert.IsType<ResetPasswordResult.WeakPassword>(result);
        _emailTokens.Verify(t => t.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownToken_ReturnsInvalidToken()
    {
        HashesTo("tok", "thash");
        _emailTokens.Setup(t => t.GetAsync("reset", "thash", It.IsAny<CancellationToken>())).ReturnsAsync((EmailTokenEntity?)null);

        Assert.IsType<ResetPasswordResult.InvalidToken>(
            await CreateSut().ExecuteAsync(new ResetPasswordCommand("tok", "newpassword1"), CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredToken_ReturnsTokenExpired_AndDeletesIt()
    {
        HashesTo("tok", "thash");
        _emailTokens.Setup(t => t.GetAsync("reset", "thash", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new EmailTokenEntity { PartitionKey = "reset", RowKey = "thash", UserId = "u-1", ExpiresAt = Now.AddHours(-1) });

        var result = await CreateSut().ExecuteAsync(new ResetPasswordCommand("tok", "newpassword1"), CancellationToken.None);

        Assert.IsType<ResetPasswordResult.TokenExpired>(result);
        _emailTokens.Verify(t => t.DeleteAsync("reset", "thash", It.IsAny<CancellationToken>()), Times.Once);
    }
}
