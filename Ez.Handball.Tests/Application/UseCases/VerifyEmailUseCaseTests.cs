using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class VerifyEmailUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEmailTokenRepository> _emailTokens = new();
    private readonly Mock<ITokenService> _tokens = new();

    private VerifyEmailUseCase CreateSut() => new(_users.Object, _emailTokens.Object, _tokens.Object, () => Now);

    private delegate bool HashCallback(string presented, out string hash);

    private void HashesTo(string presented, string hash) =>
        _tokens.Setup(t => t.TryHashEmailToken(presented, out It.Ref<string>.IsAny))
               .Returns(new HashCallback((string p, out string h) => { h = hash; return true; }));

    [Fact]
    public async Task ValidToken_MarksVerified_BumpsChangedAt_DeletesToken()
    {
        HashesTo("tok", "thash");
        _emailTokens.Setup(t => t.GetAsync("verify", "thash", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new EmailTokenEntity { PartitionKey = "verify", RowKey = "thash", UserId = "u-1", ExpiresAt = Now.AddHours(1) });
        var user = new UserEntity { RowKey = "u-1", Email = "a@b.is", EmailVerified = false };
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateSut().ExecuteAsync("tok", CancellationToken.None);

        Assert.IsType<VerifyEmailResult.Success>(result);
        Assert.True(user.EmailVerified);
        Assert.Equal(Now, user.ChangedAt);
        _users.Verify(u => u.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once);
        _emailTokens.Verify(t => t.DeleteAsync("verify", "thash", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownToken_ReturnsInvalidToken()
    {
        HashesTo("tok", "thash");
        _emailTokens.Setup(t => t.GetAsync("verify", "thash", It.IsAny<CancellationToken>())).ReturnsAsync((EmailTokenEntity?)null);

        Assert.IsType<VerifyEmailResult.InvalidToken>(await CreateSut().ExecuteAsync("tok", CancellationToken.None));
    }

    [Fact]
    public async Task MalformedToken_ReturnsInvalidToken()
    {
        _tokens.Setup(t => t.TryHashEmailToken(It.IsAny<string>(), out It.Ref<string>.IsAny)).Returns(false);
        Assert.IsType<VerifyEmailResult.InvalidToken>(await CreateSut().ExecuteAsync("bad", CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredToken_ReturnsTokenExpired_AndDeletesIt()
    {
        HashesTo("tok", "thash");
        _emailTokens.Setup(t => t.GetAsync("verify", "thash", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new EmailTokenEntity { PartitionKey = "verify", RowKey = "thash", UserId = "u-1", ExpiresAt = Now.AddHours(-1) });

        var result = await CreateSut().ExecuteAsync("tok", CancellationToken.None);

        Assert.IsType<VerifyEmailResult.TokenExpired>(result);
        _emailTokens.Verify(t => t.DeleteAsync("verify", "thash", It.IsAny<CancellationToken>()), Times.Once);
    }
}
