using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetTournamentStatusUseCaseTests
{
    private readonly Mock<ITournamentRepository> _tournaments = new();

    private GetTournamentStatusUseCase CreateSut() => new(_tournaments.Object);

    [Fact]
    public async Task ExecuteAsync_ReturnsWhateverTheRepositoryReturns()
    {
        var expected = new List<TournamentStatus>
        {
            new("8444", "Olís deild karla", "karlar", TournamentType.League,
                "olis-karla", "Olís deild karla", "2025-26", true, true, 10)
        };
        _tournaments.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync(CancellationToken.None);

        Assert.Same(expected, result);
    }
}
