using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetInviteUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IMiniLeagueInviteRepository> _invites = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private GetInviteUseCase CreateSut() => new(_leagues.Object, _invites.Object);

    private static MiniLeague League(string id = "lg-1") => new(id, "Office League", "2025-26", "u-1", Now);

    private void LeagueExists(string id = "lg-1") =>
        _leagues.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(League(id));

    private void Members(string leagueId, params string[] userIds) =>
        _leagues.Setup(r => r.GetMembersAsync(leagueId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(userIds.Select(u => new MiniLeagueMember(u, MiniLeagueRoles.Member, Now)).ToList());

    [Fact]
    public async Task MissingLeague_ReturnsLeagueNotFound()
    {
        _leagues.Setup(r => r.GetAsync("lg-x", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        Assert.IsType<GetInviteResult.LeagueNotFound>(
            await CreateSut().ExecuteAsync("u-1", "lg-x", CancellationToken.None));
    }

    [Fact]
    public async Task CallerNotMember_ReturnsNotMember()
    {
        LeagueExists();
        Members("lg-1", "someone-else");

        Assert.IsType<GetInviteResult.NotMember>(
            await CreateSut().ExecuteAsync("u-1", "lg-1", CancellationToken.None));
    }

    [Fact]
    public async Task NoInvite_ReturnsNoInvite()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeagueInvite?)null);

        Assert.IsType<GetInviteResult.NoInvite>(
            await CreateSut().ExecuteAsync("u-1", "lg-1", CancellationToken.None));
    }

    [Fact]
    public async Task Existing_ReturnsFoundWithTokenAndExpiry()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-1", "lg-1", "u-1", Now, Now.AddDays(7)));

        var found = Assert.IsType<GetInviteResult.Found>(
            await CreateSut().ExecuteAsync("u-1", "lg-1", CancellationToken.None));
        Assert.Equal("tok-1", found.Token);
        Assert.Equal(Now.AddDays(7), found.ExpiresAt);
    }
}
