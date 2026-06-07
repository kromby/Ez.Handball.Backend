using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class TeamProvisioningServiceTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameBudgetRepository> _budget = new();
    private readonly Mock<ISquadConstraintsRepository> _constraints = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private TeamProvisioningService Sut() =>
        new(_teams.Object, _budget.Object, _constraints.Object, () => Now);

    [Fact]
    public async Task Provision_WhenNoTeam_CreatesTeamWithColorAndSeedsBudgetToStartingCap()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int>(), 100_000_000, "ISK"));

        await Sut().ProvisionAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#1E88E5", default);

        _teams.Verify(t => t.CreateAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#1E88E5", Now, It.IsAny<CancellationToken>()), Times.Once);
        _budget.Verify(b => b.CreateAsync("u-1:fantasy", 100_000_000, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Provision_WhenTeamExists_DoesNothing()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Sut().ProvisionAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#1E88E5", default);

        _teams.Verify(t => t.CreateAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _budget.Verify(b => b.CreateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Provision_MissingConstraints_SeedsZeroBudget()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((SquadConstraints?)null);

        await Sut().ProvisionAsync("u-1", GameFlavor.Fantasy, "Dream Team", "#1E88E5", default);

        _budget.Verify(b => b.CreateAsync("u-1:fantasy", 0, Now, It.IsAny<CancellationToken>()), Times.Once);
    }
}
