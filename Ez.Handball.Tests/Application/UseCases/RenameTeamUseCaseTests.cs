using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class RenameTeamUseCaseTests
{
    private readonly Mock<IGameTeamRepository> _teams = new();
    private readonly Mock<IGameTeamNameIndexRepository> _nameIndex = new();
    private readonly Mock<IGetManagerUseCase> _getManager = new();

    private RenameTeamUseCase Sut() => new(_teams.Object, _nameIndex.Object, _getManager.Object);

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private void ExistingTeam(string name) => _teams.Setup(t => t.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GameTeam("u-1:fantasy", name, "#1E88E5", T0));
    private void ManagerProjection() => _getManager.Setup(g => g.ExecuteAsync("u-1", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GetManagerResult.Found(new ManagerView("New Name", "385", "#1E88E5",
            new OnboardingView(false, 0, 15))));

    [Fact]
    public async Task Blocked_ReturnsValidationError()
    {
        var result = await Sut().ExecuteAsync("u-1", "admin", default);
        var v = Assert.IsType<RenameTeamResult.ValidationError>(result);
        Assert.Equal("teamName", v.Field);
    }

    [Fact]
    public async Task EmptyName_ReturnsValidationError()
    {
        var result = await Sut().ExecuteAsync("u-1", "   ", default);
        Assert.IsType<RenameTeamResult.ValidationError>(result);
    }

    [Fact]
    public async Task NoTeam_ReturnsNoTeam()
    {
        _teams.Setup(t => t.GetAsync("u-1", GameFlavor.Fantasy, It.IsAny<CancellationToken>())).ReturnsAsync((GameTeam?)null);
        var result = await Sut().ExecuteAsync("u-1", "New Name", default);
        Assert.IsType<RenameTeamResult.NoTeam>(result);
    }

    [Fact]
    public async Task SameNormalizedName_IsNoOpSuccess_NoReservation()
    {
        ExistingTeam("Dream Team");
        ManagerProjection();
        var result = await Sut().ExecuteAsync("u-1", "  dream team ", default); // same normalized
        Assert.IsType<RenameTeamResult.Success>(result);
        _nameIndex.Verify(n => n.TryReserveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _nameIndex.Verify(n => n.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _teams.Verify(t => t.RenameAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Taken_ReturnsTeamNameTaken_OldNamePreserved()
    {
        ExistingTeam("Old Name");
        _nameIndex.Setup(n => n.TryReserveAsync("new name", "u-1:fantasy", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var result = await Sut().ExecuteAsync("u-1", "New Name", default);
        Assert.IsType<RenameTeamResult.TeamNameTaken>(result);
        _teams.Verify(t => t.RenameAsync(It.IsAny<string>(), It.IsAny<GameFlavor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _nameIndex.Verify(n => n.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HappyPath_ReservesNew_Renames_ReleasesOld()
    {
        ExistingTeam("Old Name");
        ManagerProjection();
        _nameIndex.Setup(n => n.TryReserveAsync("new name", "u-1:fantasy", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Sut().ExecuteAsync("u-1", " New Name ", default);

        var success = Assert.IsType<RenameTeamResult.Success>(result);
        Assert.Equal("New Name", success.View.TeamName);
        _teams.Verify(t => t.RenameAsync("u-1", GameFlavor.Fantasy, "New Name", It.IsAny<CancellationToken>()), Times.Once);
        _nameIndex.Verify(n => n.ReleaseAsync("old name", It.IsAny<CancellationToken>()), Times.Once);
    }
}
