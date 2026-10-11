using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class EffectiveLineupServiceTests
{
    private readonly Mock<ILineupRepository> _saved = new();
    private static readonly LineupConstraints Constraints = new(1, 7, new Dictionary<string, (int, int)>(), 2, true, false);

    private EffectiveLineupService CreateSut() => new(_saved.Object);

    private static SquadPlayer P(string id, string pos, string? secondary = null, string? playAs = null) =>
        new(id, id, "1", "Club", pos, "karlar", new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), 5,
            PositionSecondary: secondary, PlayAs: playAs);

    [Fact]
    public async Task NoSavedLineup_UsesDefaultLineup_WithPinStamped()
    {
        _saved.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);
        var squad = new[] { P("a", "FP", "GK", "GK"), P("b", "FP") };

        var result = await CreateSut().GetAsync("t", squad, Constraints, default);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Slots.Count);
        Assert.Equal("GK", result.Slots.Single(s => s.PlayerId == "a").PlayAs);
        Assert.Null(result.Slots.Single(s => s.PlayerId == "b").PlayAs);
    }

    [Fact]
    public async Task SavedLineup_KeepsRoles_AndStampsPin()
    {
        _saved.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new Lineup(new[]
        {
            new LineupSlot("a", LineupRole.Bench, 1),
            new LineupSlot("b", LineupRole.Starter, null),
        }));
        var squad = new[] { P("a", "FP", "GK", "GK"), P("b", "FP") };

        var result = await CreateSut().GetAsync("t", squad, Constraints, default);

        var a = result!.Slots.Single(s => s.PlayerId == "a");
        Assert.Equal(LineupRole.Bench, a.Role);
        Assert.Equal(1, a.BenchOrder);
        Assert.Equal("GK", a.PlayAs);
    }

    [Fact]
    public async Task StalePin_IsNotStamped()
    {
        _saved.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lineup(new[] { new LineupSlot("a", LineupRole.Starter, null) }));

        var result = await CreateSut().GetAsync("t", new[] { P("a", "FP", null, "GK") }, Constraints, default);

        Assert.Null(result!.Slots[0].PlayAs);
    }

    [Fact]
    public async Task EmptySquad_NoSavedLineup_ReturnsNull()
    {
        _saved.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);

        Assert.Null(await CreateSut().GetAsync("t", Array.Empty<SquadPlayer>(), Constraints, default));
    }

    [Fact]
    public async Task SavedSlotForPlayerNotInSquad_HasNoPin()
    {
        _saved.Setup(s => s.GetAsync("t", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lineup(new[] { new LineupSlot("gone", LineupRole.Starter, null, "GK") }));

        var result = await CreateSut().GetAsync("t", new[] { P("a", "FP") }, Constraints, default);

        Assert.Null(result!.Slots[0].PlayAs);
    }
}
