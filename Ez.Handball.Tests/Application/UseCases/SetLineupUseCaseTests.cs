using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SetLineupUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGetSquadUseCase> _squad = new();
    private readonly Mock<ILineupRepository> _lineup = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();
    private readonly Mock<IGameweekSnapshotGuard> _guard = new();

    public SetLineupUseCaseTests()
    {
        _guard.Setup(g => g.EnsureSnapshotsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new SnapshotGuardResult(null, false));
    }

    private SetLineupUseCase Sut() => new(_teams.Object, _squad.Object, _lineup.Object, _constraints.Object, _guard.Object);

    private static LineupConstraints C() => new(1, 7,
        new Dictionary<string, (int, int)> { ["GK"] = (1, 1) }, 2, true, false);

    private static SquadPlayer Player(string id, string pos) => new(
        id, $"N{id}", "385", "Stjarnan", pos, "karlar",
        new PlayerPrice(10_000_000, "ISK"), new PlayerPrice(9_000_000, "ISK"), Rating: 0);

    private static SquadView SquadOf(params SquadPlayer[] players) => new(
        players, new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"));

    // A valid 8-player owned squad and matching lineup (1 GK + 6 court starters + 1 bench).
    private static SquadView ValidSquad() => SquadOf(
        Player("p0", "GK"), Player("p1", "LW"), Player("p2", "RW"), Player("p3", "LB"),
        Player("p4", "CB"), Player("p5", "RB"), Player("p6", "LP"), Player("p7", "CB"));

    private static Lineup ValidLineup() => new(new[]
    {
        new LineupSlot("p0", LineupRole.Captain, null),
        new LineupSlot("p1", LineupRole.Starter, null),
        new LineupSlot("p2", LineupRole.Starter, null),
        new LineupSlot("p3", LineupRole.Starter, null),
        new LineupSlot("p4", LineupRole.Starter, null),
        new LineupSlot("p5", LineupRole.Starter, null),
        new LineupSlot("p6", LineupRole.Starter, null),
        new LineupSlot("p7", LineupRole.Bench, 0),
    });

    private void Arrange(bool teamExists = true)
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(teamExists);
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(C());
        _squad.Setup(s => s.ExecuteAsync("u-1", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(ValidSquad()));
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        Arrange(teamExists: false);
        var result = await Sut().ExecuteAsync("u-1", ValidLineup(), null, null, null, default);
        Assert.IsType<SetLineupResult.NoTeam>(result);
    }

    [Fact]
    public async Task ConstraintsMissing_ReturnsRuleSetNotFound()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _constraints.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((LineupConstraints?)null);

        var result = await Sut().ExecuteAsync("u-1", ValidLineup(), null, null, null, default);

        Assert.IsType<SetLineupResult.RuleSetNotFound>(result);
    }

    [Fact]
    public async Task InvalidLineup_ReturnsRejected_DoesNotPersist()
    {
        Arrange();
        // Demote the GK to bench → no GK starter, wrong starter count.
        var bad = new Lineup(ValidLineup().Slots.Select(s =>
            s.PlayerId == "p0" ? new LineupSlot("p0", LineupRole.Bench, 1) : s).ToList());

        var result = await Sut().ExecuteAsync("u-1", bad, null, null, null, default);

        var rejected = Assert.IsType<SetLineupResult.Rejected>(result);
        Assert.NotEmpty(rejected.Violations);
        _lineup.Verify(l => l.ReplaceAsync(It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidLineup_PersistsAndReturnsCommitted()
    {
        Arrange();

        var result = await Sut().ExecuteAsync("u-1", ValidLineup(), null, null, null, default);

        var committed = Assert.IsType<SetLineupResult.Committed>(result);
        Assert.True(committed.View.IsValid);
        _lineup.Verify(l => l.ReplaceAsync("u-1:fantasy", It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
