using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Tests.TestSupport;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Application.UseCases;

public class AdvanceClockUseCaseTests
{
    private readonly Mock<IClockOverrideStore> _store = new();
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1, 3);
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private AdvanceClockUseCase Sut(bool enabled = true) =>
        new(_store.Object, _config.Object, _calendar.Object, new StubTimeProvider(Now), enabled);

    private void SetupCalendar(params Gameweek[] gws)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gws);
    }

    private static Gameweek GW(int n, string round, DateTimeOffset deadline, params GameweekMatch[] m) =>
        new(n, round, "8444", deadline, GameweekStatus.Open, m);

    private static GameweekMatch Match(string id, DateTimeOffset date, bool final) =>
        new(id, date, final, "h", "a");

    [Fact]
    public async Task Disabled_WhenFlagOff_DoesNotWrite()
    {
        var result = await Sut(enabled: false).ExecuteAsync(ClockMode.Clear, null, null, default);

        Assert.IsType<AdvanceClockResult.Disabled>(result);
        _store.Verify(s => s.ClearAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Clear_DeletesOverride()
    {
        var result = await Sut().ExecuteAsync(ClockMode.Clear, null, null, default);

        Assert.IsType<AdvanceClockResult.Cleared>(result);
        _store.Verify(s => s.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_WritesSuppliedInstantAsUtc()
    {
        var target = new DateTimeOffset(2025, 10, 5, 18, 30, 0, TimeSpan.FromHours(2)); // 16:30Z

        var result = await Sut().ExecuteAsync(ClockMode.Set, target, null, default);

        var moved = Assert.IsType<AdvanceClockResult.Moved>(result);
        Assert.Equal(target.ToUniversalTime(), moved.VirtualNow);
        _store.Verify(s => s.SetAsync(target.ToUniversalTime(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceDeadline_PicksEarliestDeadlineAfterNow()
    {
        SetupCalendar(
            GW(1, "1", Now.AddHours(-1)),  // past
            GW(2, "2", Now.AddHours(5)),   // next
            GW(3, "3", Now.AddHours(50)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceDeadline, null, null, default);

        var moved = Assert.IsType<AdvanceClockResult.Moved>(result);
        Assert.Equal(Now.AddHours(5), moved.VirtualNow);
        Assert.Equal("2", moved.RoundLabel);
        _store.Verify(s => s.SetAsync(Now.AddHours(5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceDeadline_NoFutureDeadline_ReturnsNothingToAdvance()
    {
        SetupCalendar(GW(1, "1", Now.AddHours(-1)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceDeadline, null, null, default);

        Assert.IsType<AdvanceClockResult.NothingToAdvance>(result);
    }

    [Fact]
    public async Task AdvanceRound_SetsNowToLastFixturePlusBuffer_ForFirstNotAllFinalRound()
    {
        // Round 1 already all-final; round 2 not yet. Buffer = 3h (Config.MatchFinalBufferHours).
        var r2Last = Now.AddHours(10);
        SetupCalendar(
            GW(1, "1", Now.AddHours(-50), Match("a", Now.AddHours(-48), final: true)),
            GW(2, "2", Now.AddHours(8),
                Match("b", Now.AddHours(9),  final: false),
                Match("c", r2Last,           final: false)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceRound, null, null, default);

        var moved = Assert.IsType<AdvanceClockResult.Moved>(result);
        Assert.Equal(r2Last.AddHours(3), moved.VirtualNow);
        Assert.Equal("2", moved.RoundLabel);
        _store.Verify(s => s.SetAsync(r2Last.AddHours(3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceRound_AllRoundsFinal_ReturnsNothingToAdvance()
    {
        SetupCalendar(GW(1, "1", Now.AddHours(-50), Match("a", Now.AddHours(-48), final: true)));

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceRound, null, null, default);

        Assert.IsType<AdvanceClockResult.NothingToAdvance>(result);
    }

    [Fact]
    public async Task Advance_ConfigMissing_ReturnsConfigMissing()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceRound, null, null, default);

        Assert.IsType<AdvanceClockResult.ConfigMissing>(result);
    }

    [Fact]
    public async Task Advance_UnknownTournament_ReturnsCalendarUnavailable()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Gameweek>?)null);

        var result = await Sut().ExecuteAsync(ClockMode.AdvanceDeadline, null, null, default);

        Assert.IsType<AdvanceClockResult.CalendarUnavailable>(result);
    }
}
