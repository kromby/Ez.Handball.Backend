using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMiniLeagueUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private GetMiniLeagueUseCase CreateSut() => new(_leagues.Object);

    [Fact]
    public async Task MissingLeague_ReturnsNotFound_AndDoesNotQueryMembers()
    {
        _leagues.Setup(r => r.GetAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetMiniLeagueResult.NotFound>(result);
        _leagues.Verify(r => r.GetMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingLeague_ReturnsFoundWithMembers()
    {
        var league = new MiniLeague("lg-1", "Office League", "2025-26", "u-1", T0);
        _leagues.Setup(r => r.GetAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync(league);
        _leagues.Setup(r => r.GetMembersAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { new MiniLeagueMember("u-1", MiniLeagueRoles.Creator, T0) });

        var result = await CreateSut().ExecuteAsync("lg-1", CancellationToken.None);

        var found = Assert.IsType<GetMiniLeagueResult.Found>(result);
        Assert.Equal("Office League", found.View.League.Name);
        var member = Assert.Single(found.View.Members);
        Assert.Equal("u-1", member.UserId);
        Assert.Equal(MiniLeagueRoles.Creator, member.Role);
    }
}
