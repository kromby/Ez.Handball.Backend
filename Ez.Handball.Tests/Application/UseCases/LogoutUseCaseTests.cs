using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class LogoutUseCaseTests
{
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<ITokenService> _tokens = new();

    private LogoutUseCase CreateSut() => new(_refresh.Object, _tokens.Object);

    private delegate bool ParseCallback(string presented, out string userId, out string hash);

    [Fact]
    public async Task Single_DeletesThatRow()
    {
        _tokens.Setup(t => t.TryParseRefreshToken("present", out It.Ref<string>.IsAny, out It.Ref<string>.IsAny))
               .Returns(new ParseCallback((string p, out string u, out string h) => { u = "u-1"; h = "hashA"; return true; }));

        await CreateSut().ExecuteAsync("present", all: false, userId: null, CancellationToken.None);

        _refresh.Verify(r => r.DeleteAsync("u-1", "hashA", It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(r => r.DeleteAllForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task All_ClearsThePartition()
    {
        await CreateSut().ExecuteAsync(refreshToken: null, all: true, userId: "u-2", CancellationToken.None);

        _refresh.Verify(r => r.DeleteAllForUserAsync("u-2", It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Single_UnparseableToken_DoesNothing_DoesNotThrow()
    {
        _tokens.Setup(t => t.TryParseRefreshToken(It.IsAny<string>(), out It.Ref<string>.IsAny, out It.Ref<string>.IsAny)).Returns(false);

        await CreateSut().ExecuteAsync("garbage", all: false, userId: null, CancellationToken.None);

        _refresh.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
