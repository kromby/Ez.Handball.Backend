using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class BackfillMiniLeagueMembershipIndexUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private BackfillMiniLeagueMembershipIndexUseCase CreateSut() => new(_leagues.Object);

    private void AllMembersReturn(params MiniLeagueMemberRow[] rows) =>
        _leagues.Setup(r => r.GetAllMembersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    [Fact]
    public async Task DryRun_ReportsCount_ButDoesNotWrite()
    {
        AllMembersReturn(
            new MiniLeagueMemberRow("lg-1", new MiniLeagueMember("u-1", MiniLeagueRoles.Creator, Now)),
            new MiniLeagueMemberRow("lg-2", new MiniLeagueMember("u-2", MiniLeagueRoles.Creator, Now)));

        var result = await CreateSut().ExecuteAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(2, result.MembersScanned);
        Assert.Equal(0, result.Written);
        _leagues.Verify(r => r.AddMemberAsync(It.IsAny<string>(), It.IsAny<MiniLeagueMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LiveRun_ReplaysEveryRowThroughAddMember()
    {
        var row1 = new MiniLeagueMemberRow("lg-1", new MiniLeagueMember("u-1", MiniLeagueRoles.Creator, Now));
        var row2 = new MiniLeagueMemberRow("lg-2", new MiniLeagueMember("u-2", MiniLeagueRoles.Member, Now));
        AllMembersReturn(row1, row2);

        var result = await CreateSut().ExecuteAsync(dryRun: false, CancellationToken.None);

        Assert.Equal(2, result.MembersScanned);
        Assert.Equal(2, result.Written);
        _leagues.Verify(r => r.AddMemberAsync("lg-1", row1.Member, It.IsAny<CancellationToken>()), Times.Once);
        _leagues.Verify(r => r.AddMemberAsync("lg-2", row2.Member, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoMembers_ReturnsZeroCounts()
    {
        AllMembersReturn();

        var result = await CreateSut().ExecuteAsync(dryRun: false, CancellationToken.None);

        Assert.Equal(0, result.MembersScanned);
        Assert.Equal(0, result.Written);
    }
}
