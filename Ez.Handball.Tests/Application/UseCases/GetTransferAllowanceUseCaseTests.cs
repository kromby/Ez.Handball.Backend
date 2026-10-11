using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Services;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetTransferAllowanceUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameweekConfigRepository> _gameweekConfig = new();
    private readonly Mock<IGameweekCalendarService> _calendar = new();
    private readonly Mock<ITransferConfigRepository> _transferConfig = new();
    private readonly Mock<ITransferCountRepository> _counts = new();

    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly GameweekConfig GwConfig = new(1, "9142", 1, 3, 1, 3);
    private static readonly TransferConfig TxConfig = new(1, 1, 1, 4, 1);
    private const string Team = "u-1:fantasy";

    // Rounds 1–2 are past their deadlines; round 3 is the earliest Open one.
    private static readonly IReadOnlyList<Gameweek> Calendar =
    [
        new(1, "1", "9142", T0.AddDays(7), GameweekStatus.Settled, []),
        new(2, "2", "9142", T0.AddDays(14), GameweekStatus.Settled, []),
        new(3, "3", "9142", T0.AddDays(21), GameweekStatus.Open, []),
        new(4, "4", "9142", T0.AddDays(28), GameweekStatus.Open, []),
    ];

    private GetTransferAllowanceUseCase Sut() => new(
        _teams.Object, _gameweekConfig.Object, _calendar.Object, _transferConfig.Object, _counts.Object);

    private void Wired(IReadOnlyList<RoundTransferCount>? counts = null)
    {
        _teams.Setup(t => t.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new GameTeam(Team, "Liðið", "#000", T0));
        _gameweekConfig.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(GwConfig);
        _calendar.Setup(c => c.GetCalendarAsync(GwConfig, It.IsAny<CancellationToken>())).ReturnsAsync(Calendar);
        _transferConfig.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(TxConfig);
        _counts.Setup(c => c.ListAsync(Team, It.IsAny<CancellationToken>())).ReturnsAsync(counts ?? []);
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        Wired();
        _teams.Setup(t => t.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync((GameTeam?)null);

        Assert.IsType<GetTransferAllowanceResult.NoTeam>(await Sut().ExecuteAsync("u-1", null, default));
    }

    [Fact]
    public async Task NoRoundGiven_UsesTheEarliestOpenRound()
    {
        Wired([new("3", Buys: 3, Sells: 3)]);

        var found = Assert.IsType<GetTransferAllowanceResult.Found>(await Sut().ExecuteAsync("u-1", null, default));

        Assert.Equal("3", found.Allowance.RoundLabel);
        Assert.Equal(2, found.Allowance.FreeAvailable); // round 2 unused → banked
        Assert.Equal(1, found.Allowance.Extra);
        Assert.Equal(4, found.Allowance.PointsHit);
    }

    [Fact]
    public async Task RoundGiven_ComputesThatRound()
    {
        Wired([new("2", Buys: 2, Sells: 2)]);

        var found = Assert.IsType<GetTransferAllowanceResult.Found>(await Sut().ExecuteAsync("u-1", "2", default));

        Assert.Equal("2", found.Allowance.RoundLabel);
        Assert.Equal(4, found.Allowance.PointsHit);
    }

    [Fact]
    public async Task UnknownRound_ReturnsNotAvailable()
    {
        Wired();

        Assert.IsType<GetTransferAllowanceResult.NotAvailable>(await Sut().ExecuteAsync("u-1", "99", default));
    }

    [Fact]
    public async Task NoOpenRound_ReturnsNotAvailable()
    {
        Wired();
        _calendar.Setup(c => c.GetCalendarAsync(GwConfig, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Calendar.Take(2).ToList());

        Assert.IsType<GetTransferAllowanceResult.NotAvailable>(await Sut().ExecuteAsync("u-1", null, default));
    }

    [Fact]
    public async Task MissingGameweekConfig_ReturnsNotAvailable()
    {
        Wired();
        _gameweekConfig.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((GameweekConfig?)null);

        Assert.IsType<GetTransferAllowanceResult.NotAvailable>(await Sut().ExecuteAsync("u-1", null, default));
    }

    [Fact]
    public async Task MissingTransferConfig_ReturnsNotAvailable()
    {
        Wired();
        _transferConfig.Setup(c => c.GetAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((TransferConfig?)null);

        Assert.IsType<GetTransferAllowanceResult.NotAvailable>(await Sut().ExecuteAsync("u-1", null, default));
    }
}
