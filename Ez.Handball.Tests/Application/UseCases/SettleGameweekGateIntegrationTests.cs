using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Tests.TestSupport;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

// End-to-end: the REAL GameweekCalendarService feeds SettleGameweekUseCase. Proves the virtual-now
// finality gate (#95) makes settlement return NotReady for stored-"S" fixtures the clock has not
// yet passed. Downstream deps (rule sets, squad, stats, lineups) are default mocks: settlement
// short-circuits at the all-final check before reaching them.
public class SettleGameweekGateIntegrationTests
{
    private readonly Mock<IMatchRepository> _matches = new();
    private readonly Mock<IGameweekLockRepository> _locks = new();
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekLineupRepository> _snapshots = new();
    private readonly Mock<ILineupRepository> _liveLineup = new();
    private readonly Mock<IGameweekScoreRepository> _scores = new();
    private readonly Mock<IGetSquadUseCase> _squad = new();
    private readonly Mock<IPlayerStatsRepository> _stats = new();
    private readonly Mock<IScoringRuleSetRepository> _ruleSets = new();
    private readonly Mock<ILineupConstraintsRepository> _constraints = new();

    private static readonly string Team = GameTeamId.For("user", GameFlavor.Fantasy);
    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1, 3);

    private static MatchListItem M(string id, DateTimeOffset date) =>
        new(id, "1", date, Venue: null, "S",
            Home: new MatchListTeam($"h{id}", "1", "Home", null, 0),
            Away: new MatchListTeam($"a{id}", "2", "Away", null, 0));

    private SettleGameweekUseCase CreateSut(DateTimeOffset now)
    {
        var calendar = new GameweekCalendarService(
            _matches.Object, _locks.Object, new StubTimeProvider(now));
        return new SettleGameweekUseCase(
            _config.Object, calendar, _snapshots.Object, _liveLineup.Object,
            _scores.Object, _squad.Object, _stats.Object, _ruleSets.Object, _constraints.Object,
            new Ez.Handball.Application.Services.GameweekScoringService(
                new Ez.Handball.Application.RatingFunctions.FantasyPlayerRatingFunction()));
    }

    [Fact]
    public async Task StoredFinalMatches_BeforeVirtualNow_SettleReturnsNotReady()
    {
        var fixture = new DateTimeOffset(2026, 2, 1, 18, 0, 0, TimeSpan.Zero); // throw-off in the "future"
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _matches.Setup(r => r.ListByTournamentAsync("8444", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TournamentMatches("8444", "Olís deild karla", "2025-26", new[] { M("m1", fixture) }));

        // now is a full day before the fixture → date+buffer > now → not final.
        var sut = CreateSut(new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero));
        var result = await sut.ExecuteAsync("user", Team, "1", null, default);

        Assert.IsType<SettleGameweekResult.NotReady>(result);
        _scores.Verify(s => s.SaveAsync(It.IsAny<GameweekScore>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
