using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SetPlayAsUseCaseTests
{
    private const string TeamId = "u-1:fantasy";

    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameweekSnapshotGuard> _guard = new();
    private readonly Mock<IGetSquadUseCase> _squadView = new();
    private readonly Mock<ILineupRepository> _lineup = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameRosterRepository> _roster = new();
    private readonly List<string> _calls = new();

    public SetPlayAsUseCaseTests()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _guard.Setup(g => g.EnsureSnapshotsAsync(TeamId, null, It.IsAny<CancellationToken>()))
              .Callback(() => _calls.Add("guard"))
              .ReturnsAsync(new SnapshotGuardResult(null, false));
        _roster.Setup(r => r.SetPlayAsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .Callback(() => _calls.Add("write"))
               .ReturnsAsync(SetPlayAsOutcome.Updated);
        _squadView.Setup(u => u.ExecuteAsync("u-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                  .Callback(() => _calls.Add("squad"))
                  .ReturnsAsync(new GetSquadResult.Found(View()));
        _lineup.Setup(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback(() => _calls.Add("lineup"))
               .ReturnsAsync((Lineup?)null);
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>()))
               .Callback(() => _calls.Add("config"))
               .ReturnsAsync(new GameweekConfig(1, "t", 1, 1, 2, 1));
        // Version 2 proves the lineup-constraints version comes from the gameweek config.
        _constraints.Setup(c => c.GetAsync(2, It.IsAny<CancellationToken>()))
                    .Callback(() => _calls.Add("constraints"))
                    .ReturnsAsync(new LineupConstraints(2, 2, new Dictionary<string, (int Min, int Max)>
                    {
                        ["CB"] = (1, 2), ["LB"] = (0, 1)
                    }, 2.0, true, false));
    }

    private SetPlayAsUseCase Sut() => new(
        _teams.Object, _guard.Object, _squadView.Object, _lineup.Object, _constraints.Object, _config.Object,
        _roster.Object, NullLogger<SetPlayAsUseCase>.Instance);

    private static SquadPlayer Player(string id, string pos, string? second = null) => new(
        id, id, "c", "Club", pos, "karlar", new PlayerPrice(1, "ISK"), new PlayerPrice(1, "ISK"), 1.0, second);

    // p1 is dual CB/LB; p2 is a plain LB.
    private static SquadView View() => new(
        new[] { Player("p1", "CB", "LB"), Player("p2", "LB") },
        new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"));

    private static BuyPlayerContext Ctx => new(null, null, null);
    private static Lineup Saved(bool withCaptain) => new(new[]
    {
        new LineupSlot("p1", withCaptain ? LineupRole.Captain : LineupRole.Starter, null),
        new LineupSlot("p2", LineupRole.Starter, null)
    });

    [Fact]
    public async Task Set_WritesPin_AndReturnsUpdated()
    {
        var result = await Sut().ExecuteAsync("u-1", "p1", "LB", Ctx, CancellationToken.None);

        Assert.IsType<SetPlayAsResult.Updated>(result);
        _roster.Verify(r => r.SetPlayAsAsync(TeamId, "p1", "LB", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_NormalisesPosition()
    {
        var result = await Sut().ExecuteAsync("u-1", "p1", " lb ", Ctx, CancellationToken.None);

        Assert.IsType<SetPlayAsResult.Updated>(result);
        _roster.Verify(r => r.SetPlayAsAsync(TeamId, "p1", "LB", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clear_PassesNull_WithoutEligibilityOrLineupChecks()
    {
        var result = await Sut().ExecuteAsync("u-1", "p1", null, Ctx, CancellationToken.None);

        Assert.IsType<SetPlayAsResult.Updated>(result);
        _roster.Verify(r => r.SetPlayAsAsync(TeamId, "p1", null, It.IsAny<CancellationToken>()), Times.Once);
        _lineup.Verify(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam_NothingWritten()
    {
        _teams.Setup(t => t.ExistsAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.IsType<SetPlayAsResult.NoTeam>(await Sut().ExecuteAsync("u-1", "p1", "LB", Ctx, CancellationToken.None));
        _roster.Verify(r => r.SetPlayAsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlayerNotInSquad_ReturnsNotInSquad()
    {
        Assert.IsType<SetPlayAsResult.NotInSquad>(await Sut().ExecuteAsync("u-1", "p9", "LB", Ctx, CancellationToken.None));
        _roster.Verify(r => r.SetPlayAsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SquadRuleSetMissing_ReturnsRuleSetNotFound()
    {
        _squadView.Setup(u => u.ExecuteAsync("u-1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(GetSquadResult.RuleSetNotFound.Instance);

        Assert.IsType<SetPlayAsResult.RuleSetNotFound>(await Sut().ExecuteAsync("u-1", "p1", "LB", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task PositionNotNatural_ReturnsNotEligible_NothingWritten()
    {
        Assert.IsType<SetPlayAsResult.NotEligible>(await Sut().ExecuteAsync("u-1", "p1", "RW", Ctx, CancellationToken.None));
        _roster.Verify(r => r.SetPlayAsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SavedLineupBrokenByPin_ReturnsRejected_NothingWritten()
    {
        // Pinning p1 to LB leaves two LB starters (max 1) and no CB (min 1).
        _lineup.Setup(l => l.GetAsync(TeamId, It.IsAny<CancellationToken>())).ReturnsAsync(Saved(withCaptain: true));

        var result = await Sut().ExecuteAsync("u-1", "p1", "LB", Ctx, CancellationToken.None);

        var rejected = Assert.IsType<SetPlayAsResult.Rejected>(result);
        Assert.All(rejected.Violations, v => Assert.Contains(v.Code, new[] { "position_min", "position_max" }));
        Assert.NotEmpty(rejected.Violations);
        _roster.Verify(r => r.SetPlayAsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SavedLineupWithMissingCaptainAndPositionViolation_RejectsOnlyPositionCodes()
    {
        _lineup.Setup(l => l.GetAsync(TeamId, It.IsAny<CancellationToken>())).ReturnsAsync(Saved(withCaptain: false));

        var result = await Sut().ExecuteAsync("u-1", "p1", "LB", Ctx, CancellationToken.None);

        var rejected = Assert.IsType<SetPlayAsResult.Rejected>(result);
        Assert.NotEmpty(rejected.Violations);
        Assert.All(rejected.Violations, v => Assert.StartsWith("position_", v.Code));
    }

    [Fact]
    public async Task SavedLineupWithoutCaptain_DoesNotBlockAValidPin()
    {
        _lineup.Setup(l => l.GetAsync(TeamId, It.IsAny<CancellationToken>())).ReturnsAsync(Saved(withCaptain: false));

        // Pinning to the primary keeps the lineup's positions valid; the missing captain pre-exists.
        Assert.IsType<SetPlayAsResult.Updated>(await Sut().ExecuteAsync("u-1", "p1", "CB", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task RosterConflict_ReturnsConflict()
    {
        _roster.Setup(r => r.SetPlayAsAsync(TeamId, "p1", "LB", It.IsAny<CancellationToken>())).ReturnsAsync(SetPlayAsOutcome.Conflict);

        Assert.IsType<SetPlayAsResult.Conflict>(await Sut().ExecuteAsync("u-1", "p1", "LB", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task RosterNotFound_ReturnsNotInSquad()
    {
        _roster.Setup(r => r.SetPlayAsAsync(TeamId, "p1", "LB", It.IsAny<CancellationToken>())).ReturnsAsync(SetPlayAsOutcome.NotFound);

        Assert.IsType<SetPlayAsResult.NotInSquad>(await Sut().ExecuteAsync("u-1", "p1", "LB", Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Guard_RunsBeforeEveryReadAndTheWrite()
    {
        _lineup.Setup(l => l.GetAsync(TeamId, It.IsAny<CancellationToken>()))
               .Callback(() => _calls.Add("lineup"))
               .ReturnsAsync(Saved(withCaptain: true));

        await Sut().ExecuteAsync("u-1", "p1", "CB", Ctx, CancellationToken.None);

        Assert.Equal("guard", _calls[0]);
        Assert.Equal(1, _calls.Count(c => c == "guard"));
        Assert.Contains("squad", _calls);
        Assert.Contains("lineup", _calls);
        Assert.Contains("constraints", _calls);
        Assert.Equal("write", _calls[^2]); // last write precedes only the post-write squad re-read
    }
}
