using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SendMiniLeagueInviteEmailUseCaseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IMiniLeagueInviteRepository> _invites = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly AuthSettings _settings = new(
        "http://localhost/verify?token={token}", "http://localhost/reset?token={token}", "http://localhost/join?token={token}");

    private SendMiniLeagueInviteEmailUseCase CreateSut() =>
        new(_leagues.Object, _invites.Object, _users.Object, _tokens.Object, _email.Object, _settings, () => Now);

    private static MiniLeague League(string id = "lg-1") => new(id, "Office League", "2025-26", "u-1", Now);

    private void LeagueExists(string id = "lg-1") =>
        _leagues.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(League(id));

    private void Members(string leagueId, params string[] userIds) =>
        _leagues.Setup(r => r.GetMembersAsync(leagueId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(userIds.Select(u => new MiniLeagueMember(u, MiniLeagueRoles.Member, Now)).ToList());

    [Fact]
    public async Task InvalidEmail_ReturnsInvalidEmail_AndDoesNotSend()
    {
        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "not-an-email", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.InvalidEmail>(result);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingLeague_ReturnsLeagueNotFound()
    {
        _leagues.Setup(r => r.GetAsync("lg-x", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("u-1", "lg-x", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.LeagueNotFound>(result);
    }

    [Fact]
    public async Task CallerNotMember_ReturnsNotMember()
    {
        LeagueExists();
        Members("lg-1", "someone-else");

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.NotMember>(result);
    }

    [Fact]
    public async Task NoExistingInvite_GeneratesOne_AndSendsEmail()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeagueInvite?)null);
        _tokens.Setup(t => t.CreateInviteCode()).Returns("tok-new");
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "en" });

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "Friend@Example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.Sent>(result);
        _invites.Verify(r => r.AddAsync(
            It.Is<MiniLeagueInvite>(i => i.Token == "tok-new" && i.LeagueId == "lg-1" && i.CreatedByUserId == "u-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            "friend@example.com", "Jón", "Office League", "http://localhost/join?token=tok-new", "en",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingInvite_ReusesToken_DoesNotRegenerate()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-existing", "lg-1", "u-1", Now, null));
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "is" });

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.Sent>(result);
        _invites.Verify(r => r.AddAsync(It.IsAny<MiniLeagueInvite>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(t => t.CreateInviteCode(), Times.Never);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            "friend@example.com", "Jón", "Office League", "http://localhost/join?token=tok-existing", "is",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExpiredExistingInvite_IsTreatedAsAbsent_GeneratesFreshTokenAndDeletesExpired()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        var expired = new MiniLeagueInvite("tok-expired", "lg-1", "u-1", Now.AddDays(-10), Now.AddDays(-1));
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync(expired);
        _tokens.Setup(t => t.CreateInviteCode()).Returns("tok-new");
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "en" });

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.Sent>(result);
        _invites.Verify(r => r.AddAsync(
            It.Is<MiniLeagueInvite>(i => i.Token == "tok-new" && i.LeagueId == "lg-1" && i.CreatedByUserId == "u-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _invites.Verify(r => r.DeleteByTokenAsync("tok-expired", It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            "friend@example.com", "Jón", "Office League", "http://localhost/join?token=tok-new", "en",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingInvite_WithFutureExpiry_IsReused_DoesNotRegenerate()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        var stillValid = new MiniLeagueInvite("tok-existing", "lg-1", "u-1", Now.AddDays(-1), Now.AddDays(1));
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync(stillValid);
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "is" });

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.Sent>(result);
        _invites.Verify(r => r.AddAsync(It.IsAny<MiniLeagueInvite>(), It.IsAny<CancellationToken>()), Times.Never);
        _invites.Verify(r => r.DeleteByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(t => t.CreateInviteCode(), Times.Never);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            "friend@example.com", "Jón", "Office League", "http://localhost/join?token=tok-existing", "is",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SecondInviteEmail_DoesNotInvalidateFirstRecipientsLink()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-existing", "lg-1", "u-1", Now, null));
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "is" });

        await CreateSut().ExecuteAsync("u-1", "lg-1", "second-friend@example.com", CancellationToken.None);

        _invites.Verify(r => r.DeleteByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
