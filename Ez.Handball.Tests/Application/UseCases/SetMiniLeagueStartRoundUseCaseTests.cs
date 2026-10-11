using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SetMiniLeagueStartRoundUseCaseTests
{
    private readonly Mock<IMiniLeagueRepository> _leagues = new();

    private SetMiniLeagueStartRoundUseCase CreateSut() => new(_leagues.Object);

    private void LeagueExists(string? startRound = null) =>
        _leagues.Setup(l => l.GetAsync("lg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MiniLeague("lg-1", "Office", "2025-26", "creator", DateTimeOffset.UnixEpoch, startRound));

    [Fact]
    public async Task Creator_SetsTrimmedStartRound()
    {
        LeagueExists();
        _leagues.Setup(l => l.SetStartRoundAsync("lg-1", "6", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync("creator", "lg-1", " 6 ", default);

        var updated = Assert.IsType<SetMiniLeagueStartRoundResult.Updated>(result);
        Assert.Equal("6", updated.League.StartRound);
        _leagues.Verify(l => l.SetStartRoundAsync("lg-1", "6", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Creator_BlankStartRound_ClearsIt()
    {
        LeagueExists(startRound: "6");
        _leagues.Setup(l => l.SetStartRoundAsync("lg-1", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync("creator", "lg-1", "", default);

        var updated = Assert.IsType<SetMiniLeagueStartRoundResult.Updated>(result);
        Assert.Null(updated.League.StartRound);
    }

    [Fact]
    public async Task NonCreator_IsRejected_AndDoesNotWrite()
    {
        LeagueExists();

        var result = await CreateSut().ExecuteAsync("member", "lg-1", "6", default);

        Assert.IsType<SetMiniLeagueStartRoundResult.NotCreator>(result);
        _leagues.Verify(l => l.SetStartRoundAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingLeague_ReturnsNotFound()
    {
        _leagues.Setup(l => l.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("creator", "missing", "6", default);

        Assert.IsType<SetMiniLeagueStartRoundResult.NotFound>(result);
    }

    [Fact]
    public async Task LeagueDeletedBeforeWrite_ReturnsNotFound()
    {
        LeagueExists();
        _leagues.Setup(l => l.SetStartRoundAsync("lg-1", "6", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().ExecuteAsync("creator", "lg-1", "6", default);

        Assert.IsType<SetMiniLeagueStartRoundResult.NotFound>(result);
    }

    [Fact]
    public async Task TooLongStartRound_ReturnsValidationError_AndDoesNotRead()
    {
        var result = await CreateSut().ExecuteAsync("creator", "lg-1", new string('x', 41), default);

        Assert.IsType<SetMiniLeagueStartRoundResult.ValidationError>(result);
        _leagues.Verify(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
