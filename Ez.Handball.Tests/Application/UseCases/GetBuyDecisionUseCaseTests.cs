using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.BuyFunctions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetBuyDecisionUseCaseTests
{
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly Mock<IPlayerSalaryService> _salary = new();
    private readonly Mock<ISquadConstraintsRepository> _constraints = new();
    private readonly Mock<ISquadRepository> _squad = new();

    private static Player Player(string id) =>
        new(id, "Name", null, null, null, "team", "club", "Club", "karlar", "Back");

    private static PlayerSalary Salary() =>
        new("p1", new PlayerCost(20_000_000, "ISK"), 6, 8, "fantasy-price-v1");

    private static SquadConstraints Constraints() =>
        new(1, 15, new Dictionary<string, int>(), 100_000_000, "ISK");

    private GetBuyDecisionUseCase CreateSut() =>
        new(new IBuyPlayerFunction[] { new FantasyBuyPlayerFunction(), new ManagerBuyPlayerFunction() },
            _players.Object, _salary.Object, _constraints.Object, _squad.Object);

    public GetBuyDecisionUseCaseTests()
    {
        _players.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Player(id));
        _salary.Setup(s => s.GetSalaryAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Salary());
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Constraints());
        _squad.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Squad(System.Array.Empty<SquadSlot>(), 100_000_000, "ISK"));
    }

    [Fact]
    public async Task PlayerNotFound_ReturnsNotFound()
    {
        _players.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("u1", "ghost", GameFlavor.Fantasy, new BuyPlayerContext(null, null, null), default);

        Assert.IsType<BuyDecisionResult.PlayerNotFound>(result);
    }

    [Fact]
    public async Task Fantasy_MissingSalaryRuleSet_ReturnsRuleSetNotFound()
    {
        _salary.Setup(s => s.GetSalaryAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((PlayerSalary?)null);

        var result = await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Fantasy, new BuyPlayerContext(null, null, null), default);

        Assert.IsType<BuyDecisionResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task Fantasy_MissingConstraints_ReturnsRuleSetNotFound()
    {
        _constraints.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((SquadConstraints?)null);

        var result = await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Fantasy, new BuyPlayerContext(null, null, null), default);

        Assert.IsType<BuyDecisionResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task Fantasy_HappyPath_DecidedWithSalaryCostAndVersion()
    {
        var result = await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Fantasy, new BuyPlayerContext(null, null, null), default);

        var decided = Assert.IsType<BuyDecisionResult.Decided>(result);
        Assert.True(decided.Decision.Allowed);
        Assert.Equal("fantasy", decided.Decision.Flavor);
        Assert.Equal(20_000_000, decided.Decision.Cost.Amount);
        Assert.Equal("fantasy-price-v1", decided.Decision.Version);
    }

    [Fact]
    public async Task Fantasy_NullVersion_DefaultsToVersion1ForSalary()
    {
        await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Fantasy, new BuyPlayerContext("2025-26", "8444", null), default);

        _salary.Verify(s => s.GetSalaryAsync("p1", 1, "2025-26", "8444", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fantasy_ExplicitVersion_FlowsToSalaryButConstraintsStayV1()
    {
        // ruleSetVersion is the pricing axis (fantasy-price-v{n}); squad constraints are a
        // separate axis pinned to v1 in this release, not driven by ruleSetVersion.
        await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Fantasy, new BuyPlayerContext("2025-26", "8444", 3), default);

        _salary.Verify(s => s.GetSalaryAsync("p1", 3, "2025-26", "8444", It.IsAny<CancellationToken>()), Times.Once);
        _constraints.Verify(c => c.GetAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Manager_ShortCircuits_NoFantasyIo()
    {
        var result = await CreateSut().ExecuteAsync("u1", "p1", GameFlavor.Manager, new BuyPlayerContext(null, null, null), default);

        var decided = Assert.IsType<BuyDecisionResult.Decided>(result);
        Assert.Equal("manager", decided.Decision.Flavor);
        Assert.Equal("manager-v0", decided.Decision.Version);
        _salary.Verify(s => s.GetSalaryAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _constraints.Verify(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _squad.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
