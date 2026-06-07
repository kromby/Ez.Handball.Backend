using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetTournamentsUseCaseTests
{
    private readonly Mock<ITournamentRepository> _tournaments = new();
    private readonly Mock<ISeasonRepository> _seasons = new();

    private GetTournamentsUseCase CreateSut() => new(_tournaments.Object, _seasons.Object);

    [Fact]
    public async Task ExecuteAsync_ExplicitSeason_QueriesThatSeason_WithoutResolvingCurrent()
    {
        var expected = new List<Tournament> { new("8444", "Olís deild karla", "karlar", TournamentType.League, "olis-karla", "Olís deild karla") };
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2024-25", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync("2024-25", CancellationToken.None);

        Assert.Same(expected, result);
        _seasons.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NullSeason_ResolvesCurrentSeason()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false), new("2025-26", true) });
        var expected = new List<Tournament> { new("8444", "Olís deild karla", "karlar", TournamentType.League, "olis-karla", "Olís deild karla") };
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync(null, CancellationToken.None);

        Assert.Same(expected, result);
        _tournaments.Verify(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BlankSeason_ResolvesCurrentSeason()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2025-26", true) });
        _tournaments.Setup(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<Tournament>());

        await CreateSut().ExecuteAsync("   ", CancellationToken.None);

        _tournaments.Verify(r => r.ListActiveBySeasonAsync("2025-26", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoCurrentSeason_ReturnsEmpty_WithoutQueryingTournaments()
    {
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Season> { new("2024-25", false) });

        var result = await CreateSut().ExecuteAsync(null, CancellationToken.None);

        Assert.Empty(result);
        _tournaments.Verify(
            r => r.ListActiveBySeasonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
