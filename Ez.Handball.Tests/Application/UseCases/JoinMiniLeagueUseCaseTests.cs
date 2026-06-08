using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class JoinMiniLeagueUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IMiniLeagueInviteRepository> _invites = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private JoinMiniLeagueUseCase CreateSut() => new(_leagues.Object, _invites.Object, () => Now);

    private void InviteFor(string token, string leagueId, DateTimeOffset? expiresAt = null) =>
        _invites.Setup(r => r.GetByTokenAsync(token, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite(token, leagueId, "creator-x", Now, expiresAt));

    private void LeagueExists(string id) =>
        _leagues.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeague(id, "Office League", "2025-26", "creator-x", Now));

    private void Members(string leagueId, params string[] userIds) =>
        _leagues.Setup(r => r.GetMembersAsync(leagueId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(userIds.Select(u => new MiniLeagueMember(u,
                    u == "creator-x" ? MiniLeagueRoles.Creator : MiniLeagueRoles.Member, Now)).ToList());

    [Fact]
    public async Task UnknownToken_ReturnsInvalidInvite()
    {
        _invites.Setup(r => r.GetByTokenAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeagueInvite?)null);

        Assert.IsType<JoinMiniLeagueResult.InvalidInvite>(
            await CreateSut().ExecuteAsync("u-2", "nope", CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredToken_ReturnsInviteExpired_AndDoesNotJoin()
    {
        InviteFor("tok-1", "lg-1", expiresAt: Now.AddDays(-1));

        var result = await CreateSut().ExecuteAsync("u-2", "tok-1", CancellationToken.None);

        Assert.IsType<JoinMiniLeagueResult.InviteExpired>(result);
        _leagues.Verify(r => r.AddMemberAsync(It.IsAny<string>(), It.IsAny<MiniLeagueMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LeagueDeleted_ReturnsInvalidInvite()
    {
        InviteFor("tok-1", "lg-1");
        _leagues.Setup(r => r.GetAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        Assert.IsType<JoinMiniLeagueResult.InvalidInvite>(
            await CreateSut().ExecuteAsync("u-2", "tok-1", CancellationToken.None));
    }

    [Fact]
    public async Task AlreadyMember_ReturnsAlreadyMember_AndDoesNotWrite()
    {
        InviteFor("tok-1", "lg-1");
        LeagueExists("lg-1");
        Members("lg-1", "creator-x", "u-2");

        var result = await CreateSut().ExecuteAsync("u-2", "tok-1", CancellationToken.None);

        var already = Assert.IsType<JoinMiniLeagueResult.AlreadyMember>(result);
        Assert.Equal(2, already.View.Members.Count);
        _leagues.Verify(r => r.AddMemberAsync(It.IsAny<string>(), It.IsAny<MiniLeagueMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewMember_JoinsAsMember_AndReturnsViewIncludingCaller()
    {
        InviteFor("tok-1", "lg-1");
        LeagueExists("lg-1");
        Members("lg-1", "creator-x");

        var result = await CreateSut().ExecuteAsync("u-2", "tok-1", CancellationToken.None);

        var joined = Assert.IsType<JoinMiniLeagueResult.Joined>(result);
        _leagues.Verify(r => r.AddMemberAsync("lg-1",
            It.Is<MiniLeagueMember>(m => m.UserId == "u-2" && m.Role == MiniLeagueRoles.Member && m.JoinedAt == Now),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(joined.View.Members, m => m.UserId == "u-2" && m.Role == MiniLeagueRoles.Member);
        Assert.Equal(2, joined.View.Members.Count);
    }
}
