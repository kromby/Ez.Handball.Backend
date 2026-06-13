using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.RatingFunctions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SettleGameweekUseCaseTests
{
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<IGameweekLineupRepository> _snapshots = new();
    private readonly Mock<ILineupRepository> _liveLineup = new();
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private readonly Mock<IGetSquadUseCase> _squad = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<IScoringRuleSetRepository> _ruleSets = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1);
    private static readonly ScoringRuleSet Rules =
        new(GameFlavor.Fantasy, 1, 1, 0, 0, 0, 1);
    private static readonly LineupConstraints Constraints = new(
        1, 2, new Dictionary<string, (int, int)> { ["GK"] = (1, 1), ["FP"] = (1, 2) }, 2, true, false);

    private SettleGameweekUseCase CreateSut() => new(
        _config.Object, _calendar.Object, _snapshots.Object, _liveLineup.Object,
        _scores.Object, _squad.Object, _stats.Object, _ruleSets.Object, _constraints.Object,
        new GameweekScoringService(new FantasyPlayerRatingFunction()));

    private static Gameweek GW(string round, GameweekStatus status, params GameweekMatch[] m) =>
        new(1, round, "8444", DateTimeOffset.UnixEpoch, status, m);

    private static GameweekMatch Match(string id, bool final) =>
        new(id, DateTimeOffset.UnixEpoch, final, "h", "a");

    private static Lineup Lineup() => new(new[]
    {
        new LineupSlot("gk1", LineupRole.Starter, null),
        new LineupSlot("fp1", LineupRole.Captain, null),
        new LineupSlot("fp2", LineupRole.Bench, 0),
    });

    private static SquadPlayer Owned(string id, string pos) =>
        new(id, id, "1", "Club", pos, "karlar", new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), 0);

    private void SetupCommon(GameweekStatus status, bool snapshotExists)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { GW("1", status, Match("m1", final: status == GameweekStatus.Settled)) });
        _ruleSets.Setup(r => r.GetAsync(GameFlavor.Fantasy, 1, It.IsAny<CancellationToken>())).ReturnsAsync(Rules);
        _constraints.Setup(r => r.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Constraints);
        _snapshots.Setup(s => s.GetSnapshotAsync("team", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshotExists ? Lineup() : null);
        _liveLineup.Setup(s => s.GetAsync("team", It.IsAny<CancellationToken>())).ReturnsAsync(Lineup());
        _squad.Setup(s => s.ExecuteAsync("user", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSquadResult.Found(new SquadView(
                new[] { Owned("gk1", "GK"), Owned("fp1", "FP"), Owned("fp2", "FP") },
                new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"), new PlayerPrice(0, "ISK"))));
        _stats.Setup(s => s.GetByMatchAsync("m1", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new PlayerStat("gk1", "m1", "8444", null, "2025-26", "h", "Club", 0, 0, 0, 0),
            new PlayerStat("fp1", "m1", "8444", null, "2025-26", "h", "Club", 3, 0, 0, 0),
        });
    }

    [Fact]
    public async Task NotAllFinal_ReturnsNotReady_DoesNotPersist()
    {
        SetupCommon(GameweekStatus.InPlay, snapshotExists: true);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { GW("1", GameweekStatus.InPlay, Match("m1", true), Match("m2", false)) });

        var result = await CreateSut().ExecuteAsync("user", "team", "1", null, default);

        Assert.IsType<SettleGameweekResult.NotReady>(result);
        _scores.Verify(s => s.SaveAsync(It.IsAny<GameweekScore>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllFinal_Scores_Persists_CaptainDoubled()
    {
        SetupCommon(GameweekStatus.Settled, snapshotExists: true);

        var result = await CreateSut().ExecuteAsync("user", "team", "1", null, default);

        var settled = Assert.IsType<SettleGameweekResult.Settled>(result);
        Assert.Equal(1 + (4 * 2), settled.Score.Points); // gk1: 1, fp1: 4 raw × 2 captain
        _scores.Verify(s => s.SaveAsync(
            It.Is<GameweekScore>(g => g.TeamId == "team" && g.RoundLabel == "1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoSnapshot_SnapshotsLiveLineupFirst()
    {
        SetupCommon(GameweekStatus.Settled, snapshotExists: false);

        await CreateSut().ExecuteAsync("user", "team", "1", null, default);

        _snapshots.Verify(s => s.SaveSnapshotAsync("team", "1", It.IsAny<Lineup>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownRound_ReturnsNotFound()
    {
        SetupCommon(GameweekStatus.Settled, snapshotExists: true);
        var result = await CreateSut().ExecuteAsync("user", "team", "99", null, default);
        Assert.IsType<SettleGameweekResult.NotFound>(result);
    }
}
