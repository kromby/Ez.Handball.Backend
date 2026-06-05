using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetSeasonsUseCaseTests
{
    private readonly Mock<ISeasonRepository> _repo = new();

    private GetSeasonsUseCase CreateSut() => new(_repo.Object);

    [Fact]
    public async Task ExecuteAsync_ReturnsRepositoryResult()
    {
        var seasons = new List<Season> { new("2025-26", true), new("2024-25", false) };
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seasons);

        var result = await CreateSut().ExecuteAsync(CancellationToken.None);

        Assert.Same(seasons, result);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Season>());

        await CreateSut().ExecuteAsync(cts.Token);

        _repo.Verify(r => r.ListAsync(cts.Token), Times.Once);
    }
}
