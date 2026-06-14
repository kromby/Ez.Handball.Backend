using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetManagerStandingsUseCaseTests
{
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private readonly Mock<IGameTeamRepository> _teams = new();

    private GetManagerStandingsUseCase CreateSut() => new(_scores.Object, _teams.Object);

    private static GameTeam Team(string userId, string name) =>
        new($"{userId}:fantasy", name, "#abcdef", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task RanksAllManagers_AndReportsTotalCount()
    {
        _scores.Setup(s => s.ListAllSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new GameweekScoreSummary("a:fantasy", "1", 30),
                new GameweekScoreSummary("b:fantasy", "1", 50),
            });
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Team("a", "Alpha"), Team("b", "Bravo") });

        var result = await CreateSut().ExecuteAsync(offset: 0, limit: 50, default);

        Assert.Equal(2, result.Total);
        Assert.Equal("1", result.LatestRoundLabel);
        Assert.Equal("b:fantasy", result.Entries[0].TeamId);
        Assert.Equal("Bravo", result.Entries[0].TeamName);
    }

    [Fact]
    public async Task AppliesPagination()
    {
        _scores.Setup(s => s.ListAllSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new GameweekScoreSummary("a:fantasy", "1", 30),
                new GameweekScoreSummary("b:fantasy", "1", 50),
                new GameweekScoreSummary("c:fantasy", "1", 10),
            });
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Team("a", "Alpha"), Team("b", "Bravo"), Team("c", "Charlie") });

        var result = await CreateSut().ExecuteAsync(offset: 1, limit: 1, default);

        Assert.Equal(3, result.Total);     // total is the full count, not the page size
        Assert.Equal(1, result.Offset);
        Assert.Equal(1, result.Limit);
        Assert.Single(result.Entries);
        Assert.Equal("a:fantasy", result.Entries[0].TeamId); // second by total (b, a, c)
    }

    [Fact]
    public async Task NoScores_ReturnsEmptyStandings()
    {
        _scores.Setup(s => s.ListAllSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GameweekScoreSummary>());
        _teams.Setup(t => t.ListByFlavorAsync(GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GameTeam>());

        var result = await CreateSut().ExecuteAsync(0, 50, default);

        Assert.Equal(0, result.Total);
        Assert.Null(result.LatestRoundLabel);
        Assert.Empty(result.Entries);
    }
}
