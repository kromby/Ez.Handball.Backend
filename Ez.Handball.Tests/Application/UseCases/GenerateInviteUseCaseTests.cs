using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GenerateInviteUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IMiniLeagueInviteRepository> _invites = new();
    private readonly Mock<ITokenService> _tokens = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private GenerateInviteUseCase CreateSut() =>
        new(_leagues.Object, _invites.Object, _tokens.Object, () => Now);

    private static MiniLeague League(string id = "lg-1") =>
        new(id, "Office League", "2025-26", "u-1", Now);

    private void LeagueExists(string id = "lg-1") =>
        _leagues.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(League(id));

    private void Members(string leagueId, params string[] userIds) =>
        _leagues.Setup(r => r.GetMembersAsync(leagueId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(userIds.Select(u => new MiniLeagueMember(u, MiniLeagueRoles.Member, Now)).ToList());

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task ExpiryOutOfRange_ReturnsInvalidExpiry_AndDoesNotWrite(int days)
    {
        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", days, CancellationToken.None);

        Assert.IsType<GenerateInviteResult.InvalidExpiry>(result);
        _invites.Verify(r => r.AddAsync(It.IsAny<MiniLeagueInvite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingLeague_ReturnsLeagueNotFound()
    {
        _leagues.Setup(r => r.GetAsync("lg-x", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("u-1", "lg-x", null, CancellationToken.None);

        Assert.IsType<GenerateInviteResult.LeagueNotFound>(result);
    }

    [Fact]
    public async Task CallerNotMember_ReturnsNotMember_AndDoesNotWrite()
    {
        LeagueExists();
        Members("lg-1", "someone-else");

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", null, CancellationToken.None);

        Assert.IsType<GenerateInviteResult.NotMember>(result);
        _invites.Verify(r => r.AddAsync(It.IsAny<MiniLeagueInvite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HappyPath_NoExpiry_MintsAndAdds_NoDelete()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _tokens.Setup(t => t.CreateInviteCode()).Returns("tok-new");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeagueInvite?)null);

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", null, CancellationToken.None);

        var gen = Assert.IsType<GenerateInviteResult.Generated>(result);
        Assert.Equal("tok-new", gen.Token);
        Assert.Null(gen.ExpiresAt);
        _invites.Verify(r => r.AddAsync(
            It.Is<MiniLeagueInvite>(i => i.Token == "tok-new" && i.LeagueId == "lg-1"
                && i.CreatedByUserId == "u-1" && i.CreatedAt == Now && i.ExpiresAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
        _invites.Verify(r => r.DeleteByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HappyPath_WithExpiry_SetsExpiresAt()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _tokens.Setup(t => t.CreateInviteCode()).Returns("tok-new");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeagueInvite?)null);

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", 7, CancellationToken.None);

        var gen = Assert.IsType<GenerateInviteResult.Generated>(result);
        Assert.Equal(Now.AddDays(7), gen.ExpiresAt);
    }

    [Fact]
    public async Task Regenerate_AddsNew_DeletesOld()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _tokens.Setup(t => t.CreateInviteCode()).Returns("tok-new");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-old", "lg-1", "u-1", Now, null));

        await CreateSut().ExecuteAsync("u-1", "lg-1", null, CancellationToken.None);

        _invites.Verify(r => r.AddAsync(It.Is<MiniLeagueInvite>(i => i.Token == "tok-new"), It.IsAny<CancellationToken>()), Times.Once);
        _invites.Verify(r => r.DeleteByTokenAsync("tok-old", It.IsAny<CancellationToken>()), Times.Once);
    }
}
