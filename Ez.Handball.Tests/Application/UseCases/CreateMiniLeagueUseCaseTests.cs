using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class CreateMiniLeagueUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<ISeasonRepository> _seasons = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private CreateMiniLeagueUseCase CreateSut() => new(_leagues.Object, _seasons.Object, () => Now);

    private void SeasonsReturn(params Season[] s) =>
        _seasons.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(s);

    [Fact]
    public async Task BlankName_ReturnsValidationError_AndDoesNotWrite()
    {
        var result = await CreateSut().ExecuteAsync("u-1", "   ", CancellationToken.None);

        var err = Assert.IsType<CreateMiniLeagueResult.ValidationError>(result);
        Assert.Equal("name", err.Field);
        _leagues.Verify(r => r.CreateAsync(It.IsAny<MiniLeague>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NameTooLong_ReturnsValidationError()
    {
        SeasonsReturn(new Season("2025-26", true));

        var result = await CreateSut().ExecuteAsync("u-1", new string('x', 61), CancellationToken.None);

        Assert.IsType<CreateMiniLeagueResult.ValidationError>(result);
        _leagues.Verify(r => r.CreateAsync(It.IsAny<MiniLeague>(), It.IsAny<CancellationToken>()), Times.Never);
        _leagues.Verify(r => r.AddMemberAsync(It.IsAny<string>(), It.IsAny<MiniLeagueMember>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoCurrentSeason_ReturnsNoCurrentSeason_AndDoesNotWrite()
    {
        SeasonsReturn(new Season("2024-25", false));

        var result = await CreateSut().ExecuteAsync("u-1", "Office League", CancellationToken.None);

        Assert.IsType<CreateMiniLeagueResult.NoCurrentSeason>(result);
        _leagues.Verify(r => r.CreateAsync(It.IsAny<MiniLeague>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HappyPath_WritesHeaderAndCreatorMember_AndReturnsView()
    {
        SeasonsReturn(new Season("2024-25", false), new Season("2025-26", true));
        MiniLeague? captured = null;
        _leagues.Setup(r => r.CreateAsync(It.IsAny<MiniLeague>(), It.IsAny<CancellationToken>()))
                .Callback<MiniLeague, CancellationToken>((l, _) => captured = l)
                .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync("u-1", "  Office League  ", CancellationToken.None);

        var created = Assert.IsType<CreateMiniLeagueResult.Created>(result);
        Assert.NotNull(captured);
        Assert.False(string.IsNullOrEmpty(captured!.Id));
        Assert.Equal("Office League", captured.Name);  // trimmed
        Assert.Equal("2025-26", captured.Season);       // current season
        Assert.Equal("u-1", captured.CreatorUserId);
        Assert.Equal(Now, captured.CreatedAt);

        _leagues.Verify(r => r.AddMemberAsync(
            captured.Id,
            It.Is<MiniLeagueMember>(m =>
                m.UserId == "u-1" && m.Role == MiniLeagueRoles.Creator && m.JoinedAt == Now),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(captured.Id, created.View.League.Id);
        var member = Assert.Single(created.View.Members);
        Assert.Equal("u-1", member.UserId);
        Assert.Equal(MiniLeagueRoles.Creator, member.Role);
    }

    [Fact]
    public async Task MemberWriteFails_DeletesHeader_AndRethrows()
    {
        SeasonsReturn(new Season("2025-26", true));
        MiniLeague? captured = null;
        _leagues.Setup(r => r.CreateAsync(It.IsAny<MiniLeague>(), It.IsAny<CancellationToken>()))
                .Callback<MiniLeague, CancellationToken>((l, _) => captured = l)
                .Returns(Task.CompletedTask);
        _leagues.Setup(r => r.AddMemberAsync(It.IsAny<string>(), It.IsAny<MiniLeagueMember>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSut().ExecuteAsync("u-1", "Office League", CancellationToken.None));

        Assert.NotNull(captured);
        // The header write is compensated so no member-less league is left behind.
        _leagues.Verify(r => r.DeleteAsync(captured!.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
