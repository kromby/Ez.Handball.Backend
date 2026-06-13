using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.Services;

public class GameweekSnapshotGuardTests
{
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<IGameweekLockRepository> _locks = new();
    private readonly Mock<IGameweekLineupRepository> _snapshots = new();
    private readonly Mock<ILineupRepository> _liveLineup = new();
    private DateTimeOffset _now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private GameweekSnapshotGuard CreateSut() => new(
        _config.Object, _calendar.Object, _locks.Object, _snapshots.Object, _liveLineup.Object, () => _now);

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1);

    private static Gameweek GW(int n, string round, DateTimeOffset deadline, GameweekStatus status) =>
        new(n, round, "8444", deadline, status, Array.Empty<GameweekMatch>());

    private static Lineup Live() => new(new[] { new LineupSlot("p1", LineupRole.Starter, null) });

    private void Setup(params Gameweek[] gws)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>())).ReturnsAsync(gws);
        _liveLineup.Setup(l => l.GetAsync("team", It.IsAny<CancellationToken>())).ReturnsAsync(Live());
    }

    [Fact]
    public async Task PastDeadline_NoSnapshot_PinsAndSnapshots()
    {
        Setup(
            GW(1, "1", _now.AddDays(-1), GameweekStatus.Settled),  // locked, no snapshot yet
            GW(2, "2", _now.AddDays(7), GameweekStatus.Open));
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);

        var result = await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _locks.Verify(l => l.PinAsync("8444", "1", It.IsAny<DateTimeOffset>(), _now, It.IsAny<CancellationToken>()), Times.Once);
        _snapshots.Verify(s => s.SaveSnapshotAsync("team", "1", It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, result.CurrentGameweek!.Number);     // editable = earliest Open
        Assert.True(result.CurrentGameweekLocked);           // a locked GW exists
    }

    [Fact]
    public async Task ExistingSnapshot_NotReSnapshotted()
    {
        Setup(GW(1, "1", _now.AddDays(-1), GameweekStatus.Settled), GW(2, "2", _now.AddDays(7), GameweekStatus.Open));
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>())).ReturnsAsync(Live());

        await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllOpen_NoSnapshots_CurrentIsFirst_NotLocked()
    {
        Setup(GW(1, "1", _now.AddDays(2), GameweekStatus.Open), GW(2, "2", _now.AddDays(9), GameweekStatus.Open));

        var result = await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, result.CurrentGameweek!.Number);
        Assert.False(result.CurrentGameweekLocked);
    }

    [Fact]
    public async Task NoLiveLineup_SkipsSnapshot_StillReportsCurrent()
    {
        Setup(GW(1, "1", _now.AddDays(-1), GameweekStatus.Settled), GW(2, "2", _now.AddDays(7), GameweekStatus.Open));
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);
        _liveLineup.Setup(l => l.GetAsync("team", It.IsAny<CancellationToken>())).ReturnsAsync((Lineup?)null);

        var result = await CreateSut().EnsureSnapshotsAsync("team", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(2, result.CurrentGameweek!.Number);
    }
}
