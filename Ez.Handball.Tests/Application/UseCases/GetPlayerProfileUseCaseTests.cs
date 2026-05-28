using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerProfileUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();

    private GetPlayerProfileUseCase CreateSut() => new(_players.Object);

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_ReturnsNotFound()
    {
        _players
            .Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetPlayerProfileResult.NotFound>(result);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerExists_ReturnsFoundWithPlayer()
    {
        var player = new Player(
            PlayerId: "12345", Name: "Aron Pálmarsson", JerseyNumber: "23",
            DateOfBirth: new DateOnly(1990, 7, 19), Age: 35,
            TeamId: "385-karlar", ClubId: "385", ClubName: "Stjarnan", Gender: "karlar");

        _players
            .Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(player);

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerProfileResult.Found>(result);
        Assert.Same(player, found.Player);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationToken_IsPassedThroughToRepository()
    {
        using var cts = new CancellationTokenSource();
        _players
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), cts.Token))
            .ReturnsAsync((Player?)null);

        await CreateSut().ExecuteAsync("x", cts.Token);

        _players.Verify(r => r.GetByIdAsync("x", cts.Token), Times.Once);
    }
}
