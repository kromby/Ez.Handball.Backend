using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMyMiniLeaguesUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private GetMyMiniLeaguesUseCase CreateSut() => new(_leagues.Object);

    private void MembershipsFor(string userId, params MiniLeagueMembership[] memberships) =>
        _leagues.Setup(r => r.GetLeaguesForUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(memberships);

    private void LeagueExists(string id, string name = "Office League") =>
        _leagues.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeague(id, name, "2025-26", "u-1", Now));

    private void Members(string leagueId, params string[] userIds) =>
        _leagues.Setup(r => r.GetMembersAsync(leagueId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(userIds.Select(u => new MiniLeagueMember(u, MiniLeagueRoles.Member, Now)).ToList());

    [Fact]
    public async Task NoMemberships_ReturnsEmptyList()
    {
        MembershipsFor("u-1");

        var result = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HappyPath_ReturnsLeagueWithRoleAndMemberCount()
    {
        MembershipsFor("u-1", new MiniLeagueMembership("lg-1", MiniLeagueRoles.Creator, Now));
        LeagueExists("lg-1", "Office League");
        Members("lg-1", "u-1", "u-2");

        var result = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("lg-1", summary.League.Id);
        Assert.Equal("Office League", summary.League.Name);
        Assert.Equal(MiniLeagueRoles.Creator, summary.Role);
        Assert.Equal(2, summary.MemberCount);
    }

    [Fact]
    public async Task OrphanedMembership_LeagueDeleted_IsSkipped()
    {
        MembershipsFor("u-1", new MiniLeagueMembership("lg-gone", MiniLeagueRoles.Member, Now));
        _leagues.Setup(r => r.GetAsync("lg-gone", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("u-1", CancellationToken.None);

        Assert.Empty(result);
    }
}
