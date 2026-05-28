using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetPlayerHistoryUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerHistoryRepository> _history = new();

    private GetPlayerHistoryUseCase CreateSut() => new(_players.Object, _history.Object);

    private static Player AnyPlayer(string id) => new(
        PlayerId: id, Name: "X", JerseyNumber: null, DateOfBirth: null, Age: null,
        TeamId: "385-karlar", ClubId: "385", ClubName: null, Gender: "karlar");

    [Fact]
    public async Task ExecuteAsync_PlayerMissing_ReturnsNotFound_AndDoesNotCallHistoryRepo()
    {
        _players.Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetPlayerHistoryResult.NotFound>(result);
        _history.Verify(r => r.GetByPlayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerExistsWithHistory_PassesThroughWrapper()
    {
        var entry = new PlayerHistoryEntry(
            "2025", "8444", "Olís deild karla", "385", "Stjarnan",
            18, 87, 4, 17, 0,
            87.0 / 18, 4.0 / 18, 17.0 / 18, 0.0);
        var totals = new PlayerHistoryTotals(18, 87, 4, 17, 0, 87.0 / 18, 4.0 / 18, 17.0 / 18, 0.0);
        var wrapper = new PlayerHistory(new[] { entry }, totals);

        _players.Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("12345"));
        _history.Setup(r => r.GetByPlayerAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(wrapper);

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerHistoryResult.Found>(result);
        Assert.Equal("12345", found.PlayerId);
        Assert.Same(wrapper, found.History);
    }

    [Fact]
    public async Task ExecuteAsync_PlayerExistsNoHistory_ReturnsFoundWithEmptyWrapper()
    {
        var emptyWrapper = new PlayerHistory(Array.Empty<PlayerHistoryEntry>(), null);

        _players.Setup(r => r.GetByIdAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyPlayer("12345"));
        _history.Setup(r => r.GetByPlayerAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(emptyWrapper);

        var result = await CreateSut().ExecuteAsync("12345", CancellationToken.None);

        var found = Assert.IsType<GetPlayerHistoryResult.Found>(result);
        Assert.Empty(found.History.Entries);
        Assert.Null(found.History.Totals);
    }
}
