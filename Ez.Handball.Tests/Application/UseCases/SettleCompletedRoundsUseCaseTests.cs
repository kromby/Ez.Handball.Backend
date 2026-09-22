using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Tests.TestSupport;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SettleCompletedRoundsUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameweekConfig Config = new(1, "9142", 1, 1, 1, 3);

    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<ISettleRoundForAllTeamsUseCase> _settleRound = new();

    private SettleCompletedRoundsUseCase Sut() =>
        new(_config.Object, _calendar.Object, _settleRound.Object, new StubTimeProvider(Now));

    private static Gameweek Round(string label, GameweekStatus status, DateTimeOffset lastMatch) =>
        new(int.Parse(label), label, "9142", lastMatch.AddDays(-1), status, new[]
        {
            new GameweekMatch($"{label}-a", lastMatch.AddHours(-2), status == GameweekStatus.Settled, "h", "a"),
            new GameweekMatch($"{label}-b", lastMatch, status == GameweekStatus.Settled, "h", "a"),
        });

    private void SetupCalendar(params Gameweek[] rounds)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>())).ReturnsAsync(rounds);
        _settleRound.Setup(s => s.ExecuteAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string round, int? _, CancellationToken _) =>
                new SettleRoundForAllTeamsResult.Completed(new SettleRoundReport(round, 2, 2, 0, 0)));
    }

    [Fact]
    public async Task NoConfig_ReturnsConfigMissing()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);

        var result = await Sut().ExecuteAsync(null, null, default);

        Assert.IsType<SettleCompletedRoundsResult.ConfigMissing>(result);
    }

    [Fact]
    public async Task UnknownTournament_ReturnsCalendarUnavailable()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Gameweek>?)null);

        var result = await Sut().ExecuteAsync(null, null, default);

        Assert.IsType<SettleCompletedRoundsResult.CalendarUnavailable>(result);
    }

    [Fact]
    public async Task NoWindow_SettlesEveryCompleteRound_SkipsIncompleteOnes()
    {
        SetupCalendar(
            Round("1", GameweekStatus.Settled, Now.AddDays(-20)),
            Round("2", GameweekStatus.Settled, Now.AddDays(-13)),
            Round("3", GameweekStatus.InPlay, Now.AddDays(-1)),
            Round("4", GameweekStatus.Open, Now.AddDays(2)));

        var result = await Sut().ExecuteAsync(null, null, default);

        var completed = Assert.IsType<SettleCompletedRoundsResult.Completed>(result);
        Assert.Equal(new[] { "1", "2" }, completed.Rounds.Select(r => r.Round));
        _settleRound.Verify(s => s.ExecuteAsync("3", It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _settleRound.Verify(s => s.ExecuteAsync("4", It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecentWindow_OnlySettlesRoundsWhoseLastMatchIsWithinIt()
    {
        SetupCalendar(
            Round("1", GameweekStatus.Settled, Now.AddDays(-20)),
            Round("2", GameweekStatus.Settled, Now.AddDays(-6)),
            Round("3", GameweekStatus.Settled, Now.AddHours(-5)));

        var result = await Sut().ExecuteAsync(TimeSpan.FromDays(7), null, default);

        var completed = Assert.IsType<SettleCompletedRoundsResult.Completed>(result);
        Assert.Equal(new[] { "2", "3" }, completed.Rounds.Select(r => r.Round));
    }

    [Fact]
    public async Task RoundFanOutFailure_StopsAndReportsTheRound()
    {
        SetupCalendar(
            Round("1", GameweekStatus.Settled, Now.AddDays(-20)),
            Round("2", GameweekStatus.Settled, Now.AddDays(-13)));
        _settleRound.Setup(s => s.ExecuteAsync("1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettleRoundForAllTeamsResult.RuleSetMissing.Instance);

        var result = await Sut().ExecuteAsync(null, null, default);

        var failed = Assert.IsType<SettleCompletedRoundsResult.RoundFailed>(result);
        Assert.Equal("1", failed.Round);
        Assert.IsType<SettleRoundForAllTeamsResult.RuleSetMissing>(failed.Reason);
        _settleRound.Verify(s => s.ExecuteAsync("2", It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
