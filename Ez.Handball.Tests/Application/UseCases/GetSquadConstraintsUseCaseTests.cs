using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetSquadConstraintsUseCaseTests
{
    private readonly Mock<ISquadConstraintsRepository> _repo = new();

    private GetSquadConstraintsUseCase CreateSut() => new(_repo.Object);

    private static SquadConstraints AnyConstraints(int version) => new(
        version,
        MaxSquadSize: 15,
        PositionLimits: new Dictionary<string, int> { ["GK"] = 2, ["P"] = 2 },
        StartingCap: 100_000_000,
        Currency: "ISK");

    [Fact]
    public async Task OmittedVersion_ResolvesToOne()
    {
        _repo.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(AnyConstraints(1));

        var result = await CreateSut().ExecuteAsync(null, CancellationToken.None);

        var found = Assert.IsType<GetSquadConstraintsResult.Found>(result);
        Assert.Equal(1, found.Constraints.Version);
        _repo.Verify(r => r.GetAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExplicitVersion_IsForwarded()
    {
        _repo.Setup(r => r.GetAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(AnyConstraints(3));

        var result = await CreateSut().ExecuteAsync(3, CancellationToken.None);

        Assert.IsType<GetSquadConstraintsResult.Found>(result);
        _repo.Verify(r => r.GetAsync(3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepoReturnsNull_ReturnsRuleSetNotFound()
    {
        _repo.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((SquadConstraints?)null);

        var result = await CreateSut().ExecuteAsync(9, CancellationToken.None);

        Assert.IsType<GetSquadConstraintsResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task RepoReturnsConstraints_ReturnsFoundWithSameData()
    {
        var constraints = AnyConstraints(1);
        _repo.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(constraints);

        var result = await CreateSut().ExecuteAsync(null, CancellationToken.None);

        var found = Assert.IsType<GetSquadConstraintsResult.Found>(result);
        Assert.Same(constraints, found.Constraints);
    }
}
