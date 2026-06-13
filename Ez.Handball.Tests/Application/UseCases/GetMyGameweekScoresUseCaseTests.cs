using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMyGameweekScoresUseCaseTests
{
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private GetMyGameweekScoresUseCase CreateSut() => new(_scores.Object);

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
}
