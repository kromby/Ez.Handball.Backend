using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Tests.TestSupport;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetLeagueMemberGameweeksUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<IGameweekSnapshotGuard> _guard = new();
    private readonly Mock<IGameweekLineupRepository> _snapshots = new();
    private readonly Mock<IGetMyGameweekScoresUseCase> _scores = new();
    private readonly Mock<IPlayerRepository> _players = new();
    private readonly DateTimeOffset _now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1, 3);
    private const string RivalTeam = "rival:fantasy";

    private GetLeagueMemberGameweeksUseCase CreateSut() => new(
        _leagues.Object, _config.Object, _calendar.Object, _guard.Object, _snapshots.Object,
        _scores.Object, _players.Object, new StubTimeProvider(_now));

    private static Gameweek GW(int n, string round, DateTimeOffset deadline) =>
        new(n, round, "8444", deadline, GameweekStatus.Open, Array.Empty<GameweekMatch>());

    private static Lineup Lineup(params string[] ids) =>
        new(ids.Select(id => new LineupSlot(id, LineupRole.Starter, null)).ToList());

    private static Player P(string id, string name) =>
        new(id, name, "9", null, null, "t", "c", null, "M", "RB", false);

    private void League(params string[] memberIds)
    {
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniLeague("lg-1", "Office", "2025-26", "me", DateTimeOffset.UnixEpoch));
        _leagues.Setup(l => l.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberIds.Select(id => new MiniLeagueMember(id, MiniLeagueRoles.Member, DateTimeOffset.UnixEpoch)).ToList());
    }

    private void Calendar(params Gameweek[] gws)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>())).ReturnsAsync(gws);
    }

    private void Scores(params GameweekScore[] rows) =>
        _scores.Setup(s => s.ExecuteAsync("rival", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGameweekScores(rows.Sum(r => r.Points), rows, new Dictionary<string, Player>()));

    private void Snapshot(string round, Lineup lineup) =>
        _snapshots.Setup(s => s.GetSnapshotAsync(RivalTeam, round, It.IsAny<CancellationToken>())).ReturnsAsync(lineup);

    private void AssertNoTeamDataRead()
    {
        _guard.Verify(g => g.EnsureSnapshotsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _snapshots.Verify(s => s.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _scores.Verify(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingLeague_ReturnsNotFound()
    {
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        Assert.IsType<GetLeagueMemberGameweeksResult.NotFound>(result);
        AssertNoTeamDataRead();
    }

    [Fact]
    public async Task CallerNotAMember_ReturnsNotFound()
    {
        League("rival", "someone");

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        Assert.IsType<GetLeagueMemberGameweeksResult.NotFound>(result);
        AssertNoTeamDataRead();
    }

    [Fact]
    public async Task TargetNotAMember_ReturnsNotFound()
    {
        League("me", "someone");

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        Assert.IsType<GetLeagueMemberGameweeksResult.NotFound>(result);
        AssertNoTeamDataRead();
    }

    [Fact]
    public async Task MissingConfig_ReturnsConfigMissing()
    {
        League("me", "rival");
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        Assert.IsType<GetLeagueMemberGameweeksResult.ConfigMissing>(result);
    }

    [Fact]
    public async Task OnlyRoundsPastTheirDeadline_AreReturned()
    {
        League("me", "rival");
        Calendar(
            GW(1, "1", _now.AddDays(-7)),
            GW(2, "2", _now),                 // deadline exactly now → locked
            GW(3, "3", _now.AddSeconds(1)));  // one second before the deadline → still open
        Snapshot("1", Lineup("p1"));
        Snapshot("2", Lineup("p1"));
        Snapshot("3", Lineup("p1"));
        Scores();

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        var found = Assert.IsType<GetLeagueMemberGameweeksResult.Found>(result);
        Assert.Equal(new[] { "1", "2" }, found.View.Gameweeks.Select(g => g.RoundLabel));
        _snapshots.Verify(s => s.GetSnapshotAsync(RivalTeam, "3", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScoreForAnOpenRound_IsNeverReturned()
    {
        League("me", "rival");
        Calendar(GW(1, "1", _now.AddDays(-7)), GW(2, "2", _now.AddDays(7)));
        Snapshot("1", Lineup("p1"));
        Scores(
            new GameweekScore(RivalTeam, "1", 10, null, Array.Empty<GameweekPlayerScore>()),
            new GameweekScore(RivalTeam, "2", 99, null, Array.Empty<GameweekPlayerScore>()));

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        var found = Assert.IsType<GetLeagueMemberGameweeksResult.Found>(result);
        Assert.Equal("1", Assert.Single(found.View.Gameweeks).RoundLabel);
        Assert.Equal(10, found.View.RunningTotal);
    }

    [Fact]
    public async Task LockedUnsettledRound_HasLineupButNoScore_SettledRoundHasBoth()
    {
        League("me", "rival");
        Calendar(GW(1, "1", _now.AddDays(-7)), GW(2, "2", _now.AddHours(-1)));
        Snapshot("1", Lineup("p1"));
        Snapshot("2", Lineup("p2"));
        Scores(new GameweekScore(RivalTeam, "1", 12, "p1", new[]
        {
            new GameweekPlayerScore("p1", 6, 12, true, false, true, 2.0),
        }));

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        var found = Assert.IsType<GetLeagueMemberGameweeksResult.Found>(result);
        var settled = found.View.Gameweeks[0];
        var locked = found.View.Gameweeks[1];
        Assert.Equal(12, settled.Score!.Points);
        Assert.Equal("p1", Assert.Single(settled.Lineup!.Slots).PlayerId);
        Assert.Null(locked.Score);
        Assert.Equal("p2", Assert.Single(locked.Lineup!.Slots).PlayerId);
        Assert.Equal(12, found.View.RunningTotal);
    }

    [Fact]
    public async Task FreezesTheTargetsLockedRounds_BeforeReadingSnapshots()
    {
        League("me", "rival");
        Calendar(GW(1, "1", _now.AddDays(-1)));
        Scores();
        var order = new List<string>();
        _guard.Setup(g => g.EnsureSnapshotsAsync(RivalTeam, null, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("guard"))
            .ReturnsAsync(new SnapshotGuardResult(null, true));
        _snapshots.Setup(s => s.GetSnapshotAsync(RivalTeam, "1", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("read"))
            .ReturnsAsync(Lineup("p1"));

        await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        Assert.Equal(new[] { "guard", "read" }, order);
    }

    [Fact]
    public async Task LockedRoundWithNoSnapshotAndNoScore_IsOmitted()
    {
        League("me", "rival");
        Calendar(GW(1, "1", _now.AddDays(-7)), GW(2, "2", _now.AddDays(-1)));
        Snapshot("2", Lineup("p1"));   // team didn't exist yet for round 1
        Scores();

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        var found = Assert.IsType<GetLeagueMemberGameweeksResult.Found>(result);
        Assert.Equal("2", Assert.Single(found.View.Gameweeks).RoundLabel);
    }

    [Fact]
    public async Task ResolvesLineupPlayers_ReusingBreakdownPlayers_AndOnlyForReturnedRounds()
    {
        League("me", "rival");
        Calendar(GW(1, "1", _now.AddDays(-7)), GW(2, "2", _now.AddDays(-1)), GW(3, "3", _now.AddDays(7)));
        Snapshot("1", Lineup("scorer"));
        Snapshot("2", Lineup("p1", "gone"));
        _scores.Setup(s => s.ExecuteAsync("rival", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGameweekScores(0,
                new[]
                {
                    new GameweekScore(RivalTeam, "1", 5, null, new[] { new GameweekPlayerScore("scorer", 5, 5, true, false, false, 1.0) }),
                    new GameweekScore(RivalTeam, "3", 9, null, new[] { new GameweekPlayerScore("future", 9, 9, true, false, false, 1.0) }),
                },
                new Dictionary<string, Player> { ["scorer"] = P("scorer", "Already Resolved"), ["future"] = P("future", "Hidden") }));
        _players.Setup(p => p.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(P("p1", "Max Emil Stenlund"));
        _players.Setup(p => p.GetByIdAsync("gone", It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var result = await CreateSut().ExecuteAsync("lg-1", "me", "rival", null, default);

        var found = Assert.IsType<GetLeagueMemberGameweeksResult.Found>(result);
        Assert.Equal("Max Emil Stenlund", found.View.Players["p1"].Name);
        Assert.Equal("Already Resolved", found.View.Players["scorer"].Name);
        Assert.False(found.View.Players.ContainsKey("gone"));
        Assert.False(found.View.Players.ContainsKey("future"));
        _players.Verify(p => p.GetByIdAsync("scorer", It.IsAny<CancellationToken>()), Times.Never);
    }
}
