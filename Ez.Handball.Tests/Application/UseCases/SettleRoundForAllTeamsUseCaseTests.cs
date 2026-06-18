using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Application.UseCases;

public class SettleRoundForAllTeamsUseCaseTests
{
    private readonly Mock<ILineupRepository> _lineups = new();
    private readonly Mock<ISettleGameweekUseCase> _settle = new();

    private SettleRoundForAllTeamsUseCase Sut() => new(_lineups.Object, _settle.Object);

    private void SetupTeams(params string[] teamIds) =>
        _lineups.Setup(l => l.ListTeamIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamIds);

    private void SetupSettle(string userId, string teamId, SettleGameweekResult result) =>
        _settle.Setup(s => s.ExecuteAsync(userId, teamId, "1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static SettleGameweekResult.Settled Settled() =>
        new(new GameweekScore("t", "1", 0, null, Array.Empty<GameweekPlayerScore>()));

    [Fact]
    public async Task FansOutOverFantasyTeams_TalliesOutcomes()
    {
        SetupTeams("u1:fantasy", "u2:fantasy", "u3:fantasy");
        SetupSettle("u1", "u1:fantasy", Settled());
        SetupSettle("u2", "u2:fantasy", SettleGameweekResult.NotReady.Instance);
        SetupSettle("u3", "u3:fantasy", SettleGameweekResult.NoSnapshotPossible.Instance);

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal("1", report.Round);
        Assert.Equal(3, report.TeamsConsidered);
        Assert.Equal(1, report.Settled);
        Assert.Equal(1, report.NotReady);
        Assert.Equal(1, report.Skipped);
    }

    [Fact]
    public async Task IgnoresMalformedBareSuffixTeamId()
    {
        // A bare ":fantasy" id would slice to an empty userId; it must be filtered out, not settled.
        SetupTeams(":fantasy", "u1:fantasy");
        SetupSettle("u1", "u1:fantasy", Settled());

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal(1, report.TeamsConsidered);
        Assert.Equal(1, report.Settled);
        _settle.Verify(s => s.ExecuteAsync("", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IgnoresNonFantasyTeams()
    {
        SetupTeams("u1:fantasy", "u2:manager");
        SetupSettle("u1", "u1:fantasy", Settled());

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal(1, report.TeamsConsidered);
        Assert.Equal(1, report.Settled);
        _settle.Verify(s => s.ExecuteAsync("u2", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyTeamSet_ReportsZeros()
    {
        SetupTeams();

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal(0, report.TeamsConsidered);
        Assert.Equal(0, report.Settled);
    }

    [Fact]
    public async Task IdempotentReRun_AllAlreadySettled_StillReportsSettled()
    {
        SetupTeams("u1:fantasy", "u2:fantasy");
        SetupSettle("u1", "u1:fantasy", Settled()); // re-scored to same value
        SetupSettle("u2", "u2:fantasy", Settled());

        var result = await Sut().ExecuteAsync("1", null, default);

        var report = Assert.IsType<SettleRoundForAllTeamsResult.Completed>(result).Report;
        Assert.Equal(2, report.Settled);
    }

    [Fact]
    public async Task RoundNotFound_SurfacesOnceAndStops()
    {
        SetupTeams("u1:fantasy", "u2:fantasy");
        SetupSettle("u1", "u1:fantasy", SettleGameweekResult.NotFound.Instance);

        var result = await Sut().ExecuteAsync("1", null, default);

        Assert.IsType<SettleRoundForAllTeamsResult.RoundNotFound>(result);
        _settle.Verify(s => s.ExecuteAsync("u2", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfigMissing_SurfacesConfigMissing()
    {
        SetupTeams("u1:fantasy");
        SetupSettle("u1", "u1:fantasy", SettleGameweekResult.ConfigMissing.Instance);

        var result = await Sut().ExecuteAsync("1", null, default);

        Assert.IsType<SettleRoundForAllTeamsResult.ConfigMissing>(result);
    }
}
