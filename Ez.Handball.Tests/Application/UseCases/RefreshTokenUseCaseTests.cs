using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class RefreshTokenUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<ITokenService> _tokens = new();

    private RefreshTokenUseCase CreateSut() => new(_users.Object, _refresh.Object, _tokens.Object, () => Now);

    private void ParsesTo(string presented, string userId, string hash) =>
        _tokens.Setup(t => t.TryParseRefreshToken(presented, out It.Ref<string>.IsAny, out It.Ref<string>.IsAny))
               .Returns(new ParseCallback((string p, out string u, out string h) => { u = userId; h = hash; return true; }));

    private delegate bool ParseCallback(string presented, out string userId, out string hash);

    private static UserEntity User() => new() { RowKey = "u-1", Email = "a@b.is", DisplayName = "Jón" };

    [Fact]
    public async Task Valid_RotatesAndIssuesNewPair()
    {
        ParsesTo("present", "u-1", "oldhash");
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(User());
        _refresh.Setup(r => r.GetAsync("u-1", "oldhash", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RefreshTokenEntity { PartitionKey = "u-1", RowKey = "oldhash", ExpiresAt = Now.AddDays(10) });
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<UserEntity>())).Returns("ACCESS");
        _tokens.Setup(t => t.CreateRefreshToken("u-1")).Returns(new IssuedToken("newvalue", "newhash", Now.AddDays(30)));
        _tokens.Setup(t => t.AccessTokenSeconds).Returns(900);

        var result = await CreateSut().ExecuteAsync("present", CancellationToken.None);

        var success = Assert.IsType<RefreshTokenResult.Success>(result);
        Assert.Equal("newvalue", success.RefreshToken);
        _refresh.Verify(r => r.DeleteAsync("u-1", "oldhash", It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(r => r.AddAsync(It.Is<RefreshTokenEntity>(t => t.RowKey == "newhash"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Malformed_ReturnsInvalidToken()
    {
        _tokens.Setup(t => t.TryParseRefreshToken("bad", out It.Ref<string>.IsAny, out It.Ref<string>.IsAny)).Returns(false);

        Assert.IsType<RefreshTokenResult.InvalidToken>(await CreateSut().ExecuteAsync("bad", CancellationToken.None));
    }

    [Fact]
    public async Task WellFormedButUnknownHash_ReusesDetection_RevokesAll()
    {
        ParsesTo("present", "u-1", "ghosthash");
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(User());
        _refresh.Setup(r => r.GetAsync("u-1", "ghosthash", It.IsAny<CancellationToken>())).ReturnsAsync((RefreshTokenEntity?)null);

        var result = await CreateSut().ExecuteAsync("present", CancellationToken.None);

        Assert.IsType<RefreshTokenResult.InvalidToken>(result);
        _refresh.Verify(r => r.DeleteAllForUserAsync("u-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Expired_ReturnsTokenExpired_AndDeletesRow()
    {
        ParsesTo("present", "u-1", "oldhash");
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync(User());
        _refresh.Setup(r => r.GetAsync("u-1", "oldhash", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RefreshTokenEntity { PartitionKey = "u-1", RowKey = "oldhash", ExpiresAt = Now.AddDays(-1) });

        var result = await CreateSut().ExecuteAsync("present", CancellationToken.None);

        Assert.IsType<RefreshTokenResult.TokenExpired>(result);
        _refresh.Verify(r => r.DeleteAsync("u-1", "oldhash", It.IsAny<CancellationToken>()), Times.Once);
    }
}
