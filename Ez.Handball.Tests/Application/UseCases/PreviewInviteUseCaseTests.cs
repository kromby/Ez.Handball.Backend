using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class PreviewInviteUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IMiniLeagueInviteRepository> _invites = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private PreviewInviteUseCase CreateSut() => new(_leagues.Object, _invites.Object, () => Now);

    [Fact]
    public async Task UnknownToken_ReturnsInvalidInvite()
    {
        _invites.Setup(r => r.GetByTokenAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeagueInvite?)null);

        Assert.IsType<PreviewInviteResult.InvalidInvite>(
            await CreateSut().ExecuteAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredToken_ReturnsInviteExpired()
    {
        _invites.Setup(r => r.GetByTokenAsync("tok-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-1", "lg-1", "u-1", Now.AddDays(-10), Now.AddDays(-1)));

        Assert.IsType<PreviewInviteResult.InviteExpired>(
            await CreateSut().ExecuteAsync("tok-1", CancellationToken.None));
    }

    [Fact]
    public async Task LeagueDeleted_ReturnsInvalidInvite()
    {
        _invites.Setup(r => r.GetByTokenAsync("tok-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-1", "lg-1", "u-1", Now, null));
        _leagues.Setup(r => r.GetAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        Assert.IsType<PreviewInviteResult.InvalidInvite>(
            await CreateSut().ExecuteAsync("tok-1", CancellationToken.None));
    }

    [Fact]
    public async Task Valid_ReturnsFoundWithSummary()
    {
        _invites.Setup(r => r.GetByTokenAsync("tok-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-1", "lg-1", "u-1", Now, null));
        _leagues.Setup(r => r.GetAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeague("lg-1", "Office League", "2025-26", "u-1", Now));
        _leagues.Setup(r => r.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[]
                {
                    new MiniLeagueMember("u-1", MiniLeagueRoles.Creator, Now),
                    new MiniLeagueMember("u-2", MiniLeagueRoles.Member, Now)
                });

        var found = Assert.IsType<PreviewInviteResult.Found>(
            await CreateSut().ExecuteAsync("tok-1", CancellationToken.None));
        Assert.Equal("lg-1", found.LeagueId);
        Assert.Equal("Office League", found.Name);
        Assert.Equal("2025-26", found.Season);
        Assert.Equal(2, found.MemberCount);
    }
}
