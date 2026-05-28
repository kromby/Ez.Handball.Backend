using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerStatsUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();

    private GetPlayerStatsUseCase CreateSut() => new(_players.Object, _stats.Object);

    private static Player AnyPlayer(string id) => new(
        PlayerId: id, Name: "X", JerseyNumber: null, DateOfBirth: null, Age: null,
        TeamId: "385-karlar", ClubId: "385", ClubName: null, Gender: "karlar");

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_ReturnsNotFound_AndDoesNotCallStatsRepo()
    {
        _players.Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetPlayerStatsResult.NotFound>(result);
        _stats.Verify(r => r.GetByPlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerExistsNoStats_ReturnsFoundWithEmptyList()
    {
        _players.Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("12345"));
        _stats.Setup(r => r.GetByPlayerAsync("12345", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<PlayerStat>());

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Equal("12345", found.PlayerId);
        Assert.Empty(found.Stats);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerExistsWithStats_ReturnsFound()
    {
        var stats = new[]
        {
            new PlayerStat("m1", "8444", "Olís deild karla", "2025", "385-karlar", "Stjarnan", 5, 0, 1, 0)
        };

        _players.Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("12345"));
        _stats.Setup(r => r.GetByPlayerAsync("12345", It.IsAny<CancellationToken>()))
              .ReturnsAsync(stats);

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerStatsResult.Found>(result);
        Assert.Equal(stats, found.Stats);
    }
}
