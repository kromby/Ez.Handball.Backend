using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class GetMatchUseCaseTests
{
    private readonly Mock<IMatchRepository> _matches = new();
    private readonly Mock<IMatchPlayerLinesRepository> _playerLines = new();

    private GetMatchUseCase CreateSut() => new(_matches.Object, _playerLines.Object);

    private static MatchInfo AnyInfo(string id) => new(
        MatchId: id, TournamentId: "8444", TournamentName: "Olís deild karla", Season: "2025-26",
        Date: DateTimeOffset.UnixEpoch, Venue: "Ásgarður", Attendance: 412, Status: "S",
        HomeTeam: new MatchTeamInfo("385-karlar", "385", "Stjarnan", new LineScore(14, 14, 28)),
        AwayTeam: new MatchTeamInfo("390-karlar", "390", "Breiðablik", new LineScore(12, 13, 25)));

    private static IReadOnlyDictionary<string, IReadOnlyList<MatchPlayerLine>> NoLines() =>
        new Dictionary<string, IReadOnlyList<MatchPlayerLine>>();

    [Fact]
    public async Task ExecuteAsync_MatchMissing_ReturnsNotFound_AndDoesNotQueryPlayerLines()
    {
        _matches.Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>()))
                .ReturnsAsync((MatchInfo?)null);

        var result = await CreateSut().ExecuteAsync("nope", CancellationToken.None);

        Assert.IsType<GetMatchResult.NotFound>(result);
        _playerLines.Verify(r => r.GetByMatchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ComposesPlayerLinesIntoCorrectTeams()
    {
        _matches.Setup(r => r.GetByIdAsync("103414", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyInfo("103414"));
        var homeLine = new MatchPlayerLine("9912", "Jón", "7", "VS", 6, 1, 1, 0);
        var awayLine = new MatchPlayerLine("7755", "Páll", "12", "MS", 4, 0, 0, 0);
        _playerLines.Setup(r => r.GetByMatchAsync("103414", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Dictionary<string, IReadOnlyList<MatchPlayerLine>>
                    {
                        ["385-karlar"] = new[] { homeLine },
                        ["390-karlar"] = new[] { awayLine },
                    });

        var result = await CreateSut().ExecuteAsync("103414", CancellationToken.None);

        var found = Assert.IsType<GetMatchResult.Found>(result);
        Assert.Equal("103414", found.Match.MatchId);
        Assert.Equal("Olís deild karla", found.Match.TournamentName);
        Assert.Equal("Stjarnan", found.Match.HomeTeam.ClubName);
        Assert.Equal(28, found.Match.HomeTeam.Score.Final);
        Assert.Same(homeLine, Assert.Single(found.Match.HomeTeam.Players));
        Assert.Same(awayLine, Assert.Single(found.Match.AwayTeam.Players));
    }

    [Fact]
    public async Task ExecuteAsync_TeamWithNoLines_GetsEmptyPlayerList()
    {
        _matches.Setup(r => r.GetByIdAsync("103414", It.IsAny<CancellationToken>()))
                .ReturnsAsync(AnyInfo("103414"));
        _playerLines.Setup(r => r.GetByMatchAsync("103414", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(NoLines());

        var result = await CreateSut().ExecuteAsync("103414", CancellationToken.None);

        var found = Assert.IsType<GetMatchResult.Found>(result);
        Assert.Empty(found.Match.HomeTeam.Players);
        Assert.Empty(found.Match.AwayTeam.Players);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToBothRepositories()
    {
        using var cts = new CancellationTokenSource();
        _matches.Setup(r => r.GetByIdAsync("103414", cts.Token)).ReturnsAsync(AnyInfo("103414"));
        _playerLines.Setup(r => r.GetByMatchAsync("103414", cts.Token)).ReturnsAsync(NoLines());

        await CreateSut().ExecuteAsync("103414", cts.Token);

        _matches.Verify(r => r.GetByIdAsync("103414", cts.Token), Times.Once);
        _playerLines.Verify(r => r.GetByMatchAsync("103414", cts.Token), Times.Once);
    }
}
