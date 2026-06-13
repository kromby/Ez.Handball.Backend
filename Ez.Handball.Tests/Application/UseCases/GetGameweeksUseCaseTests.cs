using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetGameweeksUseCaseTests
{
    private readonly Mock<IGameweekConfigRepository> _config = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();

    private GetGameweeksUseCase CreateSut() => new(_config.Object, _calendar.Object);
    private GetCurrentGameweekUseCase CreateCurrentSut() => new(_config.Object, _calendar.Object);

    private static readonly GameweekConfig Config = new(1, "8444", 1, 1, 1);

    private static Gameweek GW(int n, GameweekStatus status, DateTimeOffset deadline) =>
        new(n, n.ToString(), "8444", deadline, status, Array.Empty<GameweekMatch>());

    private void Setup(params Gameweek[] gws)
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>())).ReturnsAsync(gws);
    }

    [Fact]
    public async Task NoConfig_ReturnsConfigMissing()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);
        var result = await CreateSut().ExecuteAsync(null, default);
        Assert.IsType<GetGameweeksResult.ConfigMissing>(result);
    }

    [Fact]
    public async Task UnknownTournament_ReturnsNotFound()
    {
        _config.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        _calendar.Setup(c => c.GetCalendarAsync(Config, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Gameweek>?)null);
        var result = await CreateSut().ExecuteAsync(null, default);
        Assert.IsType<GetGameweeksResult.NotFound>(result);
    }

    [Fact]
    public async Task ReturnsCalendar()
    {
        var t = new DateTimeOffset(2026, 1, 13, 0, 0, 0, TimeSpan.Zero);
        Setup(GW(1, GameweekStatus.Settled, t), GW(2, GameweekStatus.Open, t.AddDays(7)));
        var result = await CreateSut().ExecuteAsync(null, default);
        var found = Assert.IsType<GetGameweeksResult.Found>(result);
        Assert.Equal(2, found.Gameweeks.Count);
    }

    [Fact]
    public async Task Current_PicksEarliestNonOpenPassed_AndLastSettled()
    {
        var t = new DateTimeOffset(2026, 1, 13, 0, 0, 0, TimeSpan.Zero);
        Setup(
            GW(1, GameweekStatus.Settled, t),
            GW(2, GameweekStatus.InPlay, t.AddDays(7)),
            GW(3, GameweekStatus.Open, t.AddDays(14)),
            GW(4, GameweekStatus.Open, t.AddDays(21)));

        var result = await CreateCurrentSut().ExecuteAsync(null, default);
        var found = Assert.IsType<GetCurrentGameweekResult.Found>(result);
        Assert.Equal(3, found.Current!.Number);          // earliest Open = the editable one
        Assert.Equal(1, found.LastSettled!.Number);       // most recent Settled
    }
}
