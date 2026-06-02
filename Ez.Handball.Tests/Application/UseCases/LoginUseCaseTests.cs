using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class LoginUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();

    private LoginUseCase CreateSut() => new(
        _users.Object, _refresh.Object, _hasher.Object, _tokens.Object, () => Now);

    private static UserEntity Existing() => new()
    {
        RowKey = "u-1", Email = "a@b.is", DisplayName = "Jón", PasswordHash = "HASH"
    };

    [Fact]
    public async Task ValidCredentials_IssuesPair_AndSetsLastLogin()
    {
        _users.Setup(u => u.GetByEmailAsync("a@b.is", It.IsAny<CancellationToken>())).ReturnsAsync(Existing());
        _hasher.Setup(h => h.Verify("hunter2hunter2", "HASH")).Returns(true);
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<UserEntity>())).Returns("ACCESS");
        _tokens.Setup(t => t.CreateRefreshToken("u-1")).Returns(new IssuedToken("rvalue", "rhash", Now.AddDays(30)));
        _tokens.Setup(t => t.AccessTokenSeconds).Returns(900);

        UserEntity? updated = null;
        _users.Setup(u => u.UpdateAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()))
              .Callback<UserEntity, CancellationToken>((u, _) => updated = u);

        var result = await CreateSut().ExecuteAsync(new LoginCommand("A@B.is", "hunter2hunter2"), CancellationToken.None);

        var success = Assert.IsType<LoginResult.Success>(result);
        Assert.Equal("ACCESS", success.AccessToken);
        Assert.Equal("rvalue", success.RefreshToken);
        Assert.Equal(Now, updated!.LastLoginAt);
        _refresh.Verify(r => r.AddAsync(It.Is<RefreshTokenEntity>(t => t.RowKey == "rhash"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownEmail_ReturnsInvalidCredentials()
    {
        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((UserEntity?)null);

        var result = await CreateSut().ExecuteAsync(new LoginCommand("x@y.is", "whatever8"), CancellationToken.None);

        Assert.IsType<LoginResult.InvalidCredentials>(result);
    }

    [Fact]
    public async Task WrongPassword_ReturnsInvalidCredentials_Indistinguishable()
    {
        _users.Setup(u => u.GetByEmailAsync("a@b.is", It.IsAny<CancellationToken>())).ReturnsAsync(Existing());
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), "HASH")).Returns(false);

        var result = await CreateSut().ExecuteAsync(new LoginCommand("a@b.is", "wrongpass1"), CancellationToken.None);

        Assert.IsType<LoginResult.InvalidCredentials>(result);
        _users.Verify(u => u.UpdateAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
