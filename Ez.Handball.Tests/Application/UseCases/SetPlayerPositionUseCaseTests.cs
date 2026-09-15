using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SetPlayerPositionUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();

    private SetPlayerPositionUseCase CreateSut() => new(_players.Object);

    [Fact]
    public async Task ExecuteAsync_UnknownPositionCode_ReturnsInvalidPosition_WithoutCallingRepository()
    {
        var result = await CreateSut().ExecuteAsync("1", "XX", null, CancellationToken.None);

        Assert.IsType<SetPlayerPositionResult.InvalidPosition>(result);
        _players.Verify(r => r.SetPositionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownSecondaryPositionCode_ReturnsInvalidPosition()
    {
        var result = await CreateSut().ExecuteAsync("1", "GK", "XX", CancellationToken.None);

        Assert.IsType<SetPlayerPositionResult.InvalidPosition>(result);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerNotFound_ReturnsPlayerNotFound()
    {
        _players
            .Setup(r => r.SetPositionAsync("nope", "GK", "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync("nope", "GK", null, CancellationToken.None);

        Assert.IsType<SetPlayerPositionResult.PlayerNotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_ValidPosition_UpdatesRepository_AndReturnsOk()
    {
        _players
            .Setup(r => r.SetPositionAsync("1", "CB", "LB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync("1", "CB", "LB", CancellationToken.None);

        Assert.IsType<SetPlayerPositionResult.Ok>(result);
    }

    [Fact]
    public async Task ExecuteAsync_NullSecondary_PassesEmptyStringToRepository()
    {
        _players
            .Setup(r => r.SetPositionAsync("1", "GK", "", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync("1", "GK", null, CancellationToken.None);

        Assert.IsType<SetPlayerPositionResult.Ok>(result);
        _players.Verify(r => r.SetPositionAsync("1", "GK", "", It.IsAny<CancellationToken>()), Times.Once);
    }
}
