using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetClubsUseCaseTests
{
    private readonly Mock<IClubRepository> _repo = new();

    private GetClubsUseCase CreateSut() => new(_repo.Object);

    [Fact]
    public async Task ExecuteAsync_ReturnsRepositoryResult()
    {
        var clubs = new List<Club> { new("385", "KR", "https://logo/kr.png"), new("390", "Fram", null) };
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clubs);

        var result = await CreateSut().ExecuteAsync(CancellationToken.None);

        Assert.Same(clubs, result);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Club>());

        await CreateSut().ExecuteAsync(cts.Token);

        _repo.Verify(r => r.ListAsync(cts.Token), Times.Once);
    }
}
