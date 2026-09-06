using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayersMissingPositionUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();

    private GetPlayersMissingPositionUseCase CreateSut() => new(_players.Object);

    [Fact]
    public async Task ExecuteAsync_ReturnsPlayersFromRepository()
    {
        var expected = new List<Player>
        {
            new(PlayerId: "1", Name: "X", JerseyNumber: null, DateOfBirth: null, Age: null,
                TeamId: "385-karlar", ClubId: "385", ClubName: "Stjarnan", Gender: "karlar",
                Position: "", Retired: false)
        };
        _players
            .Setup(r => r.ListMissingPositionAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync(null, null, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsClubIdAndGenderToRepository()
    {
        _players
            .Setup(r => r.ListMissingPositionAsync("385", "karlar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player>());

        await CreateSut().ExecuteAsync("385", "karlar", CancellationToken.None);

        _players.Verify(r => r.ListMissingPositionAsync("385", "karlar", It.IsAny<CancellationToken>()), Times.Once);
    }
}
