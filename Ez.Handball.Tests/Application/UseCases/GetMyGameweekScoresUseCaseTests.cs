using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMyGameweekScoresUseCaseTests
{
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private GetMyGameweekScoresUseCase CreateSut() => new(_scores.Object, _players.Object);

    [Fact]
    public async Task SumsRunningTotal_AcrossGameweeks()
    {
        _scores.Setup(s => s.ListByTeamAsync("user:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new GameweekScore("user:fantasy", "1", 40, "fp1", Array.Empty<GameweekPlayerScore>()),
                new GameweekScore("user:fantasy", "2", 55, "fp2", Array.Empty<GameweekPlayerScore>()),
            });

        var result = await CreateSut().ExecuteAsync("user", default);

        Assert.Equal(95, result.RunningTotal);
        Assert.Equal(2, result.Gameweeks.Count);
    }

    [Fact]
    public async Task ResolvesBreakdownPlayers_EvenWhenNotInCurrentSquad()
    {
        _scores.Setup(s => s.ListByTeamAsync("user:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new GameweekScore("user:fantasy", "3", 7, null, new[]
                {
                    new GameweekPlayerScore("p1", 7, 7, true, false, false, 1.0),
                    new GameweekPlayerScore("gone", 1, 1, true, false, false, 1.0),
                }),
                new GameweekScore("user:fantasy", "4", 7, null, new[]
                {
                    new GameweekPlayerScore("p1", 7, 7, true, false, false, 1.0),
                }),
            });
        _players.Setup(p => p.GetByIdAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Player("p1", "Max Emil Stenlund", "9", null, null, "t", "c", null, "M", "RB", false));
        _players.Setup(p => p.GetByIdAsync("gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("user", default);

        Assert.Equal("Max Emil Stenlund", result.Players["p1"].Name);
        Assert.False(result.Players.ContainsKey("gone"));
        _players.Verify(p => p.GetByIdAsync("p1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
