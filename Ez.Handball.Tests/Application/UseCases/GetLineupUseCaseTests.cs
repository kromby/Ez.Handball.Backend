using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetLineupUseCaseTests
{
    private readonly Mock<IGetSquadUseCase> _squad = new();
    private readonly Mock<ILineupRepository> _lineup = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();

    private GetLineupUseCase Sut() => new(_squad.Object, _lineup.Object, _constraints.Object);

    private static LineupConstraints C() => new(1, 7,
        new Dictionary<string, (int, int)> { ["GK"] = (1, 1) }, 2, true, false);

    private static SquadPlayer Player(string id, string pos) => new(
        id, $"N{id}", "385", "Stjarnan", pos, "karlar",
        new PlayerPrice(10_000_000, "ISK"), new PlayerPrice(9_000_000, "ISK"), 0);

    private static SquadView SquadOf(params SquadPlayer[] players) => new(
        players, new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"));

    [Fact]
    public async Task ConstraintsMissing_ReturnsRuleSetNotFound()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((LineupConstraints?)null);

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        Assert.IsType<GetLineupResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task NoStoredLineup_ReturnsNotSet_WithMultiplier()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _lineup.Setup(l => l.GetAsync("u-1:fantasy", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        var notSet = Assert.IsType<GetLineupResult.NotSet>(result);
        Assert.Equal(2, notSet.CaptainMultiplier);
    }

    [Fact]
    public async Task StoredLineup_ReturnsFound_WithValidityAnnotated()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _lineup.Setup(l => l.GetAsync("u-1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lineup(new[] { new LineupSlot("p0", LineupRole.Captain, null) }));
        _squad.Setup(s => s.ExecuteAsync("u-1", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(SquadOf(Player("p0", "GK"))));

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        var found = Assert.IsType<GetLineupResult.Found>(result);
        Assert.Single(found.View.Slots);
        // One GK starter but only 1 of 7 starters → invalid; the view still returns.
        Assert.False(found.View.IsValid);
    }

    [Fact]
    public async Task SquadRuleSetNotFound_PropagatesRuleSetNotFound()
    {
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _lineup.Setup(l => l.GetAsync("u-1:fantasy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lineup(new[] { new LineupSlot("p0", LineupRole.Starter, null) }));
        _squad.Setup(s => s.ExecuteAsync("u-1", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GetSquadResult.RuleSetNotFound.Instance);

        var result = await Sut().ExecuteAsync("u-1", null, null, null, default);

        Assert.IsType<GetLineupResult.RuleSetNotFound>(result);
    }
}
