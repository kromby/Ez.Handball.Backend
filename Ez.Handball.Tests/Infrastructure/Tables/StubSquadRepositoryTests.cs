using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class StubSquadRepositoryTests
{
    private readonly Mock<ISquadConstraintsRepository> _constraints = new();

    private StubSquadRepository CreateSut() => new(_constraints.Object);

    [Fact]
    public async Task Get_NewUser_EmptySquadWithStartingCapBudget()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SquadConstraints(1, 15, new Dictionary<string, int>(), 100_000_000, "ISK"));

        var squad = await CreateSut().GetAsync("user-1", GameFlavor.Fantasy, default);

        Assert.Empty(squad.Players);
        Assert.Equal(100_000_000, squad.Budget);
        Assert.Equal("ISK", squad.Currency);
    }

    [Fact]
    public async Task Get_MissingConstraints_FallsBackToZeroBudget()
    {
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((SquadConstraints?)null);

        var squad = await CreateSut().GetAsync("user-1", GameFlavor.Fantasy, default);

        Assert.Empty(squad.Players);
        Assert.Equal(0, squad.Budget);
        Assert.Equal("ISK", squad.Currency);
    }
}
